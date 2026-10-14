using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;
public sealed class QuickReviewTests
{
    private const string Password = "Synthetic-Quick-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> Login(ApiFactory factory,string email)
    {
        var client=factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/auth/login",new{email,password=Password},Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }

    /// <summary>Seeds a clean document with an AI reading and an untouched draft built from it.</summary>
    private static ReceiptTransaction Seed(KuTaxDbContext db,string owner,string merchant,DateOnly date,decimal amount,decimal confidence=.95m,string? category=null,string provider="Groq")
    {
        var batch=new ImportBatch{OwnerId=owner,ExpectedFiles=1};
        var document=new ReceiptDocument{OwnerId=owner,ImportBatchId=batch.Id,OriginalFileName=merchant+".pdf",RelativePath=merchant+".pdf",ObjectKey="k-"+Guid.NewGuid(),Sha256=Guid.NewGuid().ToString("N"),ContentType="application/pdf",ScanStatus="Clean",Status="NeedsReview"};
        var candidate=new ReceiptCandidate(new(merchant,confidence,merchant),new(date.ToString("yyyy-MM-dd"),confidence,"Date"),new(amount,confidence,"Total"),new(null,0,null),"AUD",Category:category is null?null:new(category,.9m,"Item"));
        db.AddRange(batch,document,new ExtractionAttempt{DocumentId=document.Id,OwnerId=owner,Provider=provider,Model="m",CandidateJson=JsonSerializer.Serialize(candidate,ReceiptCandidate.Json)});
        var transaction=new ReceiptTransaction{DocumentId=document.Id,OwnerId=owner,Merchant=merchant,Date=date,Amount=amount,SuggestedCategory=category};
        db.Add(transaction);
        return transaction;
    }

    [Fact]
    public async Task Clean_ai_readings_are_confirmed_allocated_and_approved_in_one_owner_action()
    {
        using var factory=new ApiFactory();
        var owner=await factory.CreateUserAsync("owner@kutax.test",Password);
        await factory.CreateUserAsync("stranger@kutax.test",Password);
        ReceiptTransaction course,app,groceries,unsure,phone,manual,bigTool;
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            course=Seed(db,owner.Id,"University of Sydney",new(2025,8,4),1450.00m,category:"SelfEducation");
            app=Seed(db,owner.Id,"Anysphere",new(2025,9,1),29.99m,category:"SoftwareSubscriptions");
            groceries=Seed(db,owner.Id,"Woolworths",new(2025,9,2),88.10m,category:"Personal");
            phone=Seed(db,owner.Id,"Telstra",new(2025,10,1),90.00m);
            bigTool=Seed(db,owner.Id,"JB Hi-Fi",new(2025,10,3),899.00m,category:"ToolsEquipment");
            unsure=Seed(db,owner.Id,"Blurry Cafe",new(2025,9,3),12.50m,confidence:.4m);
            manual=Seed(db,owner.Id,"Typed By Hand",new(2025,9,4),10m,provider:"Manual");
            Seed(db,owner.Id,"Other Year Pty",new(2024,9,4),10m);
            Seed(db,owner.Id,"No Date Co",new(2025,9,5),15m).Date=null;
            foreach(var rule in TaxRuleCatalog.Rules.Where(r=>r.FinancialYearStart==2025&&r.Category!="PhoneInternet"))
                db.TaxRuleReviews.Add(new(){RuleVersion=rule.Version,ActorId="synthetic-admin",ReviewedAt=DateTimeOffset.UtcNow,Note="Synthetic review"});
            await db.SaveChangesAsync(Ct);
        }
        using var client=await Login(factory,"owner@kutax.test");
        var list=await client.GetFromJsonAsync<JsonElement>("/api/quick-review/2025",Ct);
        var ids=list.GetProperty("items").EnumerateArray().Select(i=>i.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(new[]{course.Id,app.Id,groceries.Id,phone.Id,bigTool.Id}.ToHashSet(),ids); // low confidence, manual and other-year receipts are left out
        Assert.Equal(3,list.GetProperty("needsCheck").GetInt32());
        var reasons=list.GetProperty("checks").EnumerateArray().ToDictionary(c=>c.GetProperty("merchant").GetString()!,c=>c.GetProperty("reason").GetString());
        Assert.Equal("AI wasn't sure of the merchant.",reasons["Blurry Cafe"]);
        Assert.Equal("Typed in by hand: check and confirm it.",reasons["Typed By Hand"]);
        Assert.Equal("The date is missing, so its financial year isn't known.",reasons["No Date Co"]);
        Assert.Equal("SelfEducation",list.GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("id").GetGuid()==course.Id).GetProperty("suggestedCategory").GetString());
        var selfEducation=list.GetProperty("rules").EnumerateArray().Single(r=>r.GetProperty("category").GetString()=="SelfEducation");
        Assert.Equal(("D4 D",true),(selfEducation.GetProperty("paperReference").GetString(),selfEducation.GetProperty("reviewed").GetBoolean()));

        object Item(ReceiptTransaction t,string category,decimal workUse=100,string? claimType=null)=>new{transactionId=t.Id,version=t.Version,category,workUsePercent=workUse,claimType};
        // Every calculated category needs its conditions confirmed, and self-education needs a claim type.
        var undeclared=await client.PostAsJsonAsync("/api/quick-review/2025/approve",new{items=new[]{Item(course,"SelfEducation",claimType:"K")},declarations=Array.Empty<string>(),confirm=true},Ct);
        Assert.Equal(HttpStatusCode.BadRequest,undeclared.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/quick-review/2025/approve",new{items=new[]{Item(course,"SelfEducation")},declarations=new[]{"SelfEducation"},confirm=true},Ct)).StatusCode);
        // Receipts outside the quick list can't be slipped into a batch.
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/quick-review/2025/approve",new{items=new[]{Item(unsure,"SoftwareSubscriptions")},declarations=new[]{"SoftwareSubscriptions"},confirm=true},Ct)).StatusCode);
        using(var stranger=await Login(factory,"stranger@kutax.test"))
            Assert.Equal(HttpStatusCode.Conflict,(await stranger.PostAsJsonAsync("/api/quick-review/2025/approve",new{items=new[]{Item(app,"SoftwareSubscriptions")},declarations=new[]{"SoftwareSubscriptions"},confirm=true},Ct)).StatusCode);

        var response=await client.PostAsJsonAsync("/api/quick-review/2025/approve",new{
            items=new[]{Item(course,"SelfEducation",claimType:"K"),Item(app,"SoftwareSubscriptions",80),Item(groceries,"Personal"),Item(phone,"PhoneInternet",40),Item(bigTool,"ToolsEquipment",100)},
            declarations=new[]{"SelfEducation","SoftwareSubscriptions","PhoneInternet","ToolsEquipment"},confirm=true},Ct);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((2,1473.99m,1),(result.GetProperty("approved").GetInt32(),result.GetProperty("approvedTotal").GetDecimal(),result.GetProperty("skipped").GetInt32())); // 1450 + 29.99 × 80% = 23.99
        var waiting=result.GetProperty("notApproved").EnumerateArray().ToDictionary(r=>r.GetProperty("transactionId").GetGuid(),r=>r.GetProperty("status").GetString());
        Assert.Equal("UnverifiedRule",waiting[phone.Id]); // no administrator review for that rule yet
        Assert.Equal("Unsupported",waiting[bigTool.Id]); // over $300: decline in value isn't calculated
        Assert.Equal(0,result.GetProperty("keptForAdvice").GetInt32());

        var claims=await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/claims",Ct);
        Assert.Equal(1473.99m,claims.GetProperty("approvedTotal").GetDecimal());
        var sections=claims.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(new[]{"D4 D","D5 E"},sections.Select(s=>s.GetProperty("paperReference").GetString()));
        Assert.Equal(("K",1450m),(sections[0].GetProperty("claimType").GetString(),sections[0].GetProperty("entryAmount").GetDecimal()));
        Assert.Equal(1473m,claims.GetProperty("entryAmount").GetDecimal()); // 1450 + 23 (cents dropped per label)

        using var check=factory.Services.CreateScope();
        var verify=check.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        Assert.Equal("Dismissed",(await verify.Transactions.SingleAsync(t=>t.Id==groceries.Id,Ct)).Status);
        Assert.Equal("Confirmed",(await verify.Transactions.SingleAsync(t=>t.Id==course.Id,Ct)).Status);
        var allocation=await verify.Allocations.SingleAsync(a=>a.TransactionId==course.Id&&a.IsActive,Ct);
        Assert.Equal(("K",true,"Approved"),(allocation.ClaimType,allocation.EligibilityConfirmed,allocation.Status));
        Assert.StartsWith("Confirmed in quick review:",allocation.Basis);
        Assert.Contains(await verify.AuditEvents.Where(a=>a.Action=="ClaimApproved").Select(a=>a.DetailsJson).ToListAsync(Ct),d=>d.Contains("QuickReview"));
        // Approved receipts leave the quick list; a repeat submission is refused.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/quick-review/2025",Ct)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/quick-review/2025/approve",new{items=new[]{Item(app,"SoftwareSubscriptions",80)},declarations=new[]{"SoftwareSubscriptions"},confirm=true},Ct)).StatusCode);
    }

    [Fact]
    public async Task Administrators_can_review_a_year_of_rules_together()
    {
        using var factory=new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test",Password);
        await factory.CreateUserAsync("member@kutax.test",Password);
        var versions=TaxRuleCatalog.Rules.Where(r=>r.FinancialYearStart==2025).Select(r=>r.Version).ToArray();
        using(var member=await Login(factory,"member@kutax.test"))
            Assert.Equal(HttpStatusCode.Forbidden,(await member.PostAsJsonAsync("/api/tax-rules/review-batch",new{versions,confirm=true,note="Not allowed"},Ct)).StatusCode);
        using var admin=await Login(factory,"admin@kutax.test");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync("/api/tax-rules/review-batch",new{versions=new[]{"AU-2099-Nope-v1"},confirm=true,note="x"},Ct)).StatusCode);
        var first=await admin.PostAsJsonAsync("/api/tax-rules/review-batch",new{versions=versions.Take(2).ToArray(),confirm=true,note="Synthetic review of fees"},Ct);
        Assert.Equal(2,(await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reviewed").GetArrayLength());
        var rest=await admin.PostAsJsonAsync("/api/tax-rules/review-batch",new{versions,confirm=true,note="Synthetic review of the rest"},Ct);
        Assert.Equal(versions.Length-2,(await rest.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reviewed").GetArrayLength());
        using var scope=factory.Services.CreateScope();
        Assert.Equal(versions.Length,await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().TaxRuleReviews.CountAsync(Ct));
    }

    [Fact]
    public void Category_suggestions_keep_only_known_categories_for_asked_indexes()
    {
        var found=CategorySuggester.Parse("""{"items":[{"index":0,"category":"SelfEducation"},{"index":1,"category":"Bribes"},{"index":2,"category":null},{"index":9,"category":"Personal"},{"index":0,"category":"Personal"}]}""",3);
        Assert.Equal(new Dictionary<int,string>{[0]="SelfEducation"},found);
        var json=JsonSerializer.Serialize(new ReceiptCandidate(new("Uni",.9m,"Uni"),new(null,0,null),new(null,0,null),new(null,0,null),"AUD",Category:new("Gambling",.9m,"x")),ReceiptCandidate.Json);
        Assert.Null(ReceiptCandidate.Sanitize(json,new DateOnly(2026,10,11)).Category);
    }

    [Fact]
    public async Task One_receipt_is_claimed_in_one_step_and_approved_only_under_a_checked_rule_with_its_conditions_confirmed()
    {
        using var factory=new ApiFactory();
        var owner=await factory.CreateUserAsync("owner@kutax.test",Password);
        ReceiptTransaction app,phone,other,draft;
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            app=Seed(db,owner.Id,"Anysphere",new(2025,9,1),29.99m);phone=Seed(db,owner.Id,"Telstra",new(2025,10,1),90.00m);
            other=Seed(db,owner.Id,"Synthetic Parking",new(2025,9,2),12.00m);draft=Seed(db,owner.Id,"Not Yet Checked",new(2025,9,3),5m);
            foreach(var t in new[]{app,phone,other}){t.Status="Confirmed";t.ConfirmedAt=DateTimeOffset.UtcNow;}
            foreach(var rule in TaxRuleCatalog.Rules.Where(r=>r.FinancialYearStart==2025&&r.Category!="PhoneInternet"))
                db.TaxRuleReviews.Add(new(){RuleVersion=rule.Version,ActorId="synthetic-admin",ReviewedAt=DateTimeOffset.UtcNow,Note="Synthetic review"});
            await db.SaveChangesAsync(Ct);
        }
        using var client=await Login(factory,"owner@kutax.test");
        object Claim(ReceiptTransaction t,string category,decimal use=100,bool declared=true)=>new{version=t.Version,category,workUsePercent=use,claimType=(string?)null,declared};

        // The conditions must be confirmed; then it's claimed and approved at once: 80% of $29.99.
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/transactions/{app.Id}/claim",Claim(app,"SoftwareSubscriptions",80,declared:false),Ct)).StatusCode);
        var done=await (await client.PostAsJsonAsync($"/api/transactions/{app.Id}/claim",Claim(app,"SoftwareSubscriptions",80),Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((true,"Approved",23.99m,2025),(done.GetProperty("approved").GetBoolean(),done.GetProperty("status").GetString(),done.GetProperty("deductibleAmount").GetDecimal(),done.GetProperty("year").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/transactions/{app.Id}/claim",Claim(app,"SoftwareSubscriptions"),Ct)).StatusCode); // already claimed

        // A rule nobody has checked yet: claimed, but it waits for the check instead of being approved.
        var waiting=await (await client.PostAsJsonAsync($"/api/transactions/{phone.Id}/claim",Claim(phone,"PhoneInternet",50),Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((false,"UnverifiedRule"),(waiting.GetProperty("approved").GetBoolean(),waiting.GetProperty("status").GetString()));
        // Something KuTax doesn't calculate is kept for advice; an unconfirmed receipt can't be claimed yet.
        var advice=await (await client.PostAsJsonAsync($"/api/transactions/{other.Id}/claim",Claim(other,"Unsupported"),Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((false,"Unsupported"),(advice.GetProperty("approved").GetBoolean(),advice.GetProperty("status").GetString()));
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/transactions/{draft.Id}/claim",Claim(draft,"SoftwareSubscriptions"),Ct)).StatusCode);
    }
}
