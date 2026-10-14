using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KuTax.Infrastructure.Processing;
using KuTax.Core.Claims;

namespace KuTax.IntegrationTests;
public sealed class ReceiptIntegrationTests
{
    [RequiresServices]
    public async Task Nested_import_is_idempotent_and_owner_scoped_in_postgres_and_rustfs()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>(); await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = NewUser(); var other = NewUser(); const string password = "Synthetic-Receipt-Test-481!";
        Assert.True((await users.CreateAsync(owner,password)).Succeeded); Assert.True((await users.CreateAsync(other,password)).Succeeded);
        var testReviews=new List<string>();
        try
        {
            using var client = CreateClient(factory); using var stranger = CreateClient(factory);
            await Login(client,owner.Email!,password); await Login(stranger,other.Email!,password);
            var created = await client.PostAsJsonAsync("/api/import-batches/",new {sourceKind="Folder",expectedFiles=3});
            Assert.Equal(HttpStatusCode.Created,created.StatusCode);
            var batch = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var bytes = Encoding.UTF8.GetBytes($"%PDF-1.4\n% Synthetic receipt {Guid.NewGuid():N}\n%%EOF"); var item = Guid.NewGuid();
            var stored = await Upload(client,batch,item,"nested/work/receipt.pdf",bytes);
            Assert.Equal("Stored",stored.GetProperty("status").GetString()); var document=stored.GetProperty("documentId").GetGuid();
            Assert.Equal(document,(await Upload(client,batch,item,"nested/work/receipt.pdf",bytes)).GetProperty("documentId").GetGuid());
            Assert.Equal("Duplicate",(await Upload(client,batch,Guid.NewGuid(),"another/copy.pdf",bytes)).GetProperty("status").GetString());
            if(Environment.GetEnvironmentVariable("KUTAX_APP_URL") is null)Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync($"/api/documents/{document}/content")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/documents/{document}/content")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/import-batches/{batch}")).StatusCode);
            using var invalid = Request(batch,Guid.NewGuid(),"../escape.pdf",bytes);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.SendAsync(invalid)).StatusCode);
            var summary = await client.GetFromJsonAsync<JsonElement>($"/api/import-batches/{batch}");
            Assert.Equal(1,summary.GetProperty("stored").GetInt32()); Assert.Equal(1,summary.GetProperty("duplicates").GetInt32());
            Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync($"/api/import-batches/{batch}/complete",new {})).StatusCode);
            if(Environment.GetEnvironmentVariable("KUTAX_APP_URL") is null)
            {
                // Simulate a worker process disappearing while holding a lease, then recover in a new worker instance.
                await db.ProcessingJobs.Where(j=>j.DocumentId==document).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Processing").SetProperty(j=>j.LeaseUntil,DateTimeOffset.UtcNow.AddMinutes(-1)).SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddYears(-1)));
                var worker=new ReceiptWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ReceiptWorker>.Instance);
                Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
                Assert.Equal(bytes,await client.GetByteArrayAsync($"/api/documents/{document}/content"));
                var review=await client.GetFromJsonAsync<JsonElement>($"/api/documents/{document}/review");
                Assert.Equal("Complete",review.GetProperty("job").GetProperty("status").GetString());
                var transaction=review.GetProperty("transaction");var transactionId=transaction.GetProperty("id").GetGuid();
                var correction=new{version=transaction.GetProperty("version").GetGuid(),merchant="Manually confirmed synthetic merchant",date="2025-08-01",amount=110m,gst=10m,notes="Checked original",confirm=true};
                Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/transactions/{transactionId}",correction)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/transactions/{transactionId}",correction)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound,(await stranger.PutAsJsonAsync($"/api/transactions/{transactionId}",correction)).StatusCode);
                Assert.Equal(HttpStatusCode.Accepted,(await client.PostAsJsonAsync($"/api/documents/{document}/retry",new{})).StatusCode);
                await db.ProcessingJobs.Where(j=>j.DocumentId==document).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddYears(-1)));
                Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
                var retained=await client.GetFromJsonAsync<JsonElement>($"/api/documents/{document}/review");
                Assert.Equal("Manually confirmed synthetic merchant",retained.GetProperty("transaction").GetProperty("merchant").GetString());
                Assert.Equal("Confirmed",retained.GetProperty("transaction").GetProperty("status").GetString());
                Assert.Equal(2,retained.GetProperty("candidates").GetArrayLength());
                var version=retained.GetProperty("transaction").GetProperty("version").GetGuid();
                var allocations=new[]{new{description="Periodic union fee",category="UnionFees",amount=80m,workUsePercent=100m,workReimbursement=0m,basis="Current employment; paid in this FY; no reimbursement",eligibilityConfirmed=true},new{description="Professional subscription",category="ProfessionalMembership",amount=30m,workUsePercent=100m,workReimbursement=0m,basis="Current employment periodic subscription for this FY",eligibilityConfirmed=true}};
                var allocated=await client.PutAsJsonAsync($"/api/transactions/{transactionId}/allocations",new{transactionVersion=version,items=allocations});
                Assert.Equal(HttpStatusCode.OK,allocated.StatusCode);
                var allocationResult=await allocated.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2,allocationResult.GetProperty("items").GetArrayLength());
                Assert.Equal(110m,allocationResult.GetProperty("items").EnumerateArray().Sum(a=>a.GetProperty("amount").GetDecimal()));
                Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/transactions/{transactionId}/allocations")).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/transactions/{transactionId}/allocations",new{transactionVersion=version,items=allocations})).StatusCode);
                var excess=allocations.Select(a=>a with { amount=111m }).ToArray();
                Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync($"/api/transactions/{transactionId}/allocations",new{transactionVersion=allocationResult.GetProperty("transactionVersion").GetGuid(),items=excess})).StatusCode);
                var audited=await client.GetFromJsonAsync<JsonElement>($"/api/documents/{document}/review");
                Assert.Contains(audited.GetProperty("history").EnumerateArray(),e=>e.GetProperty("action").GetString()=="AllocationsReplaced"&&e.GetProperty("actorId").GetString()==owner.Id);
                Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/tax-rules/AU-2025-UnionFees-v1/review",new{confirm=true,note="Non-admin cannot review rules"})).StatusCode);
                foreach(var rule in TaxRuleCatalog.Rules.Where(r=>r.FinancialYearStart==2025))
                    if(!await db.TaxRuleReviews.AnyAsync(r=>r.RuleVersion==rule.Version))
                    {db.TaxRuleReviews.Add(new(){RuleVersion=rule.Version,ActorId=owner.Id,ReviewedAt=DateTimeOffset.UtcNow,Note="Explicit synthetic editorial review for integration fixture"});testReviews.Add(rule.Version);}
                await db.SaveChangesAsync();
                var year=await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/claims");
                Assert.Equal(0m,year.GetProperty("approvedTotal").GetDecimal());Assert.Equal(110m,year.GetProperty("candidateTotal").GetDecimal());
                var claimRow=year.GetProperty("rows")[0];var allocationId=claimRow.GetProperty("allocationId").GetGuid();
                var approval=new{allocationVersion=claimRow.GetProperty("allocationVersion").GetGuid(),transactionVersion=claimRow.GetProperty("transactionVersion").GetGuid(),ruleVersion=claimRow.GetProperty("ruleVersion").GetString(),confirm=true};
                Assert.Equal(HttpStatusCode.NotFound,(await stranger.PostAsJsonAsync($"/api/allocations/{allocationId}/approve",approval)).StatusCode);
                Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/allocations/{allocationId}/approve",approval)).StatusCode);
                var approvedYear=await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/claims");
                Assert.Equal(claimRow.GetProperty("deductibleAmount").GetDecimal(),approvedYear.GetProperty("approvedTotal").GetDecimal());
                var exportId=await ExportAssertions.CreateAndVerify(client,stranger,db,factory.Services,claimRow.GetProperty("deductibleAmount").GetDecimal());
                var evidence=new{version=approval.allocationVersion,documents=new[]{document},note="Additional synthetic supporting record"};
                Assert.Equal(HttpStatusCode.NotFound,(await stranger.PutAsJsonAsync($"/api/allocations/{allocationId}/evidence",evidence)).StatusCode);
                Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/allocations/{allocationId}/evidence",evidence)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/allocations/{allocationId}/approve",approval)).StatusCode);
                var invalidated=await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/claims");Assert.Equal(0m,invalidated.GetProperty("approvedTotal").GetDecimal());
                Assert.Equal(1,await db.TaxClaims.CountAsync(c=>c.OwnerId==owner.Id)); // historical approval retained
                var frozen=await client.GetFromJsonAsync<JsonElement>($"/api/tax-exports/{exportId}");Assert.Equal(claimRow.GetProperty("deductibleAmount").GetDecimal(),frozen.GetProperty("model").GetProperty("assessment").GetProperty("approvedTotal").GetDecimal());
            }
        }
        finally
        {
            var storage=scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            foreach(var document in await db.Documents.Where(d=>d.OwnerId==owner.Id).ToListAsync()) await storage.DeleteAsync(document.ObjectKey,CancellationToken.None);
            foreach(var job in await db.ExportJobs.AsNoTracking().Where(j=>j.OwnerId==owner.Id).ToListAsync())foreach(var file in JsonSerializer.Deserialize<ExportFile[]>(job.FilesJson,ReceiptCandidate.Json)??[])await storage.DeleteAsync(file.ObjectKey,CancellationToken.None);
            await db.EntryProgress.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();await db.ExportJobs.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();await db.ExportSnapshots.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ProcessingJobs.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.AuditEvents.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ExtractionAttempts.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.TaxClaims.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.EvidenceLinks.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.TaxRuleReviews.Where(r=>testReviews.Contains(r.RuleVersion)&&r.ActorId==owner.Id).ExecuteDeleteAsync();
            await db.Allocations.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.Transactions.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ImportItems.Where(i=>db.ImportBatches.Any(b=>b.Id==i.BatchId&&b.OwnerId==owner.Id)).ExecuteDeleteAsync();
            await db.Documents.Where(d=>d.OwnerId==owner.Id).ExecuteDeleteAsync(); await db.ImportBatches.Where(b=>b.OwnerId==owner.Id).ExecuteDeleteAsync();
            Assert.True((await users.DeleteAsync(owner)).Succeeded); Assert.True((await users.DeleteAsync(other)).Succeeded);
        }
    }
    private static ApplicationUser NewUser() { var email=$"receipt-{Guid.NewGuid():N}@kutax.test"; return new(){UserName=email,Email=email}; }
    private static HttpRequestMessage Request(Guid batch,Guid item,string path,byte[] bytes)
    {
        var request=new HttpRequestMessage(HttpMethod.Put,$"/api/import-batches/{batch}/items/{item}"){Content=new ByteArrayContent(bytes)};
        request.Headers.Add("X-File-Name","receipt.pdf"); request.Headers.Add("X-Relative-Path",Uri.EscapeDataString(path)); return request;
    }
    internal static async Task<JsonElement> Upload(HttpClient client,Guid batch,Guid item,string path,byte[] bytes)
    { using var request=Request(batch,item,path,bytes); var response=await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK,response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    internal static HttpClient CreateClient(WebApplicationFactory<Program> factory) => Environment.GetEnvironmentVariable("KUTAX_APP_URL") is {Length:>0} url
        ? new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer(),AllowAutoRedirect=false}){BaseAddress=new Uri(url)} : factory.CreateClient();
    internal static async Task Login(HttpClient client,string email,string password)
    { await Csrf(client); Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/auth/login",new{email,password})).StatusCode); await Csrf(client); }
    internal static async Task Csrf(HttpClient client)
    {
        var response=await client.GetAsync("/api/auth/csrf"); var cookie=response.Headers.GetValues("Set-Cookie").Single(c=>c.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-XSRF-TOKEN",Uri.UnescapeDataString(cookie.Split(';')[0][11..]));
    }
}
