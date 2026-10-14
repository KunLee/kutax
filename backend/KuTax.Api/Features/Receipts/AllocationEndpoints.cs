using System.Security.Claims;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Receipts;
public static class AllocationEndpoints
{
    public static void MapAllocations(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/transactions").RequireAuthorization();
        group.MapGet("/{id:guid}/allocations",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);if(!await db.Transactions.AnyAsync(t=>t.Id==id&&t.OwnerId==owner,ct))return Results.NotFound();
            return Results.Ok(await db.Allocations.AsNoTracking().Where(a=>a.TransactionId==id&&a.OwnerId==owner&&a.IsActive).OrderBy(a=>a.Id).ToListAsync(ct));
        });
        // Everything KuTax can fill in for a first expense: the category (asking the AI from the merchant name when it hasn't
        // suggested one yet, with the owner's consent) and the work use and claim type the owner last approved for it.
        group.MapPost("/{id:guid}/prefill",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,IAiOptionsSource settings,ICategorySuggester suggester,CancellationToken ct)=>
        {
            var owner=Owner(principal);var transaction=await db.Transactions.SingleOrDefaultAsync(t=>t.Id==id&&t.OwnerId==owner,ct);
            if(transaction is null)return Results.NotFound();
            if(transaction.SuggestedCategory is null&&!string.IsNullOrWhiteSpace(transaction.Merchant))
            {
                var options=await settings.GetAsync(ct);var notice=AiPrivacy.NoticeVersion(options);
                if(AiPrivacy.Configured(options)&&await db.PrivacyPreferences.AnyAsync(p=>p.OwnerId==owner&&p.ExternalAiConsent&&p.NoticeVersion==notice,ct))
                    try
                    {
                        if((await suggester.SuggestAsync([transaction.Merchant],ct))?.GetValueOrDefault(0) is {} found){transaction.SuggestedCategory=found;await db.SaveChangesAsync(ct);}
                    }
                    catch(Exception error) when(error is HttpRequestException or InvalidDataException or JsonException or ProviderRateLimitedException or TaskCanceledException){/* a suggestion is optional */}
            }
            var category=transaction.SuggestedCategory is "Personal"?"Excluded":transaction.SuggestedCategory;
            var history=await db.Allocations.AsNoTracking().Where(a=>a.OwnerId==owner&&a.ApprovedAt!=null&&a.Category==category).Select(a=>new{a.WorkUsePercent,a.ClaimType,a.ApprovedAt}).ToListAsync(ct);
            var last=history.MaxBy(a=>a.ApprovedAt);
            return Results.Ok(new{category,workUsePercent=last?.WorkUsePercent,claimType=last?.ClaimType});
        });
        group.MapPut("/{id:guid}/allocations",async(Guid id,AllocationSet request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var transaction=await db.Transactions.SingleOrDefaultAsync(t=>t.Id==id&&t.OwnerId==owner,ct);
            if(transaction is null)return Results.NotFound();
            if(transaction.Version!=request.TransactionVersion)return Results.Problem("Receipt changed. Reload before saving allocations.",statusCode:409);
            if(transaction.Status!="Confirmed"||transaction.Amount is null)return Results.Problem("Confirm the receipt facts first.",statusCode:409);
            var ruleSet=await db.LoadRulesAsync(ct);
            if(Problem(request.Items,transaction.Amount.Value,ruleSet) is {} problem)return Results.Problem(problem,statusCode:400);
            var year=transaction.Date is {} date?date.Year-(date.Month<7?1:0):(int?)null;
            var old=await db.Allocations.Where(a=>a.TransactionId==id&&a.OwnerId==owner&&a.IsActive).ToListAsync(ct);
            var before=JsonSerializer.Serialize(old,ReceiptCandidate.Json);
            foreach(var allocation in old){allocation.IsActive=false;allocation.Version=Guid.NewGuid();}
            var allocations=request.Items.Select(a=>new ExpenseAllocation{OwnerId=owner,TransactionId=id,Description=a.Description.Trim(),Category=a.Category,ClaimType=a.ClaimType,Amount=a.Amount,WorkUsePercent=a.WorkUsePercent,WorkReimbursement=a.WorkReimbursement,Basis=Basis(a,year is {} y?ruleSet.Find(y,a.Category):null),EligibilityConfirmed=a.EligibilityConfirmed,Status=a.Category=="Excluded"?"Excluded":"Pending"}).ToArray();
            db.Allocations.AddRange(allocations);transaction.Version=Guid.NewGuid();
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),ResourceId=id,Action="AllocationsReplaced",DetailsJson=JsonSerializer.Serialize(new{before=JsonDocument.Parse(before).RootElement,after=allocations},ReceiptCandidate.Json)});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}
            return Results.Ok(new{transactionVersion=transaction.Version,items=allocations});
        });
    }
    /// <summary>The first thing wrong with the allocations, said plainly and naming the expense; null when they're valid.</summary>
    public static string? Problem(AllocationInput[]? items,decimal receiptTotal,TaxRuleSet rules)
    {
        if(items is null||items.Length==0)return "Add at least one expense.";
        if(items.Length>50)return "Use at most 50 expenses per receipt.";
        for(var i=0;i<items.Length;i++)
        {
            var a=items[i];var at=items.Length==1?"":$"Expense {i+1}: ";
            if(!rules.Categories.Contains(a.Category))return at+"choose a category.";
            if(string.IsNullOrWhiteSpace(a.Description))return at+"add a short description.";
            if(a.Description.Length>200)return at+"keep the description under 200 characters.";
            if(a.Basis?.Length>2000)return at+"keep the notes under 2,000 characters.";
            var claimTypes=rules.All.Where(r=>r.Category==a.Category).SelectMany(r=>r.ClaimTypes).Distinct().ToArray();
            if(a.ClaimType is not null&&!claimTypes.Contains(a.ClaimType))return at+"that claim type doesn't apply to this category.";
            if(a.Amount<=0||a.Amount>1_000_000||decimal.Round(a.Amount,2)!=a.Amount)return at+"enter an amount above $0 with at most two decimal places.";
            if(a.WorkUsePercent is <0 or >100||decimal.Round(a.WorkUsePercent,2)!=a.WorkUsePercent)return at+"work use must be between 0 and 100%.";
            if(a.WorkReimbursement<0||decimal.Round(a.WorkReimbursement,2)!=a.WorkReimbursement)return at+"enter the reimbursed amount in dollars and cents, or 0.";
            var work=decimal.Round(a.Amount*a.WorkUsePercent/100,2,MidpointRounding.AwayFromZero);
            if(a.WorkReimbursement>work)return at+$"the reimbursement can't be more than the work portion (${work:0.00}).";
        }
        var total=items.Sum(a=>a.Amount);
        return total>receiptTotal?$"The expenses add up to ${total:0.00}, more than the receipt total of ${receiptTotal:0.00}.":null;
    }

    /// <summary>The owner's own words when given; otherwise what their choice already says (the confirmed declaration, or why it isn't claimed).</summary>
    public static string Basis(AllocationInput input,TaxRule? rule)=>!string.IsNullOrWhiteSpace(input.Basis)?input.Basis.Trim()
        :input.Category=="Excluded"?"Private or excluded by the owner."
        :input.Category=="Unsupported"?"Kept for advice: KuTax doesn't calculate this kind of expense."
        :input.EligibilityConfirmed&&rule is not null?"Confirmed: "+rule.Declaration
        :"";
    private static string Owner(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Owner(principal);
    private static string Actor(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Actor(principal);
}
public sealed record AllocationSet(Guid TransactionVersion,AllocationInput[] Items);
public sealed record AllocationInput(string Description,string Category,decimal Amount,decimal WorkUsePercent,decimal WorkReimbursement,string? Basis,bool EligibilityConfirmed,string? ClaimType=null);
