using System.Security.Claims;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Claims;

/// <summary>
/// Quick review: receipts that AI read cleanly can be confirmed, allocated in full and approved in one owner action.
/// AI only suggests categories; the owner picks the category, work use and claim type, confirms each category's
/// conditions, and approves the batch. Anything unusual stays in the one-by-one review.
/// </summary>
public static class QuickReviewEndpoints
{
    // AI readings below this confidence are checked one by one.
    internal const decimal MinConfidence = 0.8m;
    private const int MaxBatch = 200;

    public static void MapQuickReview(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/transactions/{id:guid}/claim",async(Guid id,ClaimOne request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            if(!ReceiptCandidate.SuggestableCategories.Contains(request.Category)||request.Category=="Personal")return Results.Problem("Choose what kind of expense this is.",statusCode:400);
            if(request.WorkUsePercent is <0 or >100||decimal.Round(request.WorkUsePercent,2)!=request.WorkUsePercent)return Results.Problem("Work use must be 0–100%.",statusCode:400);
            var owner=Owner(principal);
            await using var dbTransaction=await db.Database.BeginTransactionAsync(ct);
            var transaction=await db.Transactions.SingleOrDefaultAsync(t=>t.Id==id&&t.OwnerId==owner,ct);
            if(transaction is null)return Results.NotFound();
            if(transaction.Version!=request.Version)return Results.Problem("This receipt changed. Reload it and try again.",statusCode:409);
            if(transaction.Status!="Confirmed"||transaction.Date is null||transaction.Amount is null||transaction.Merchant is null)return Results.Problem("Confirm the receipt details first.",statusCode:409);
            if(await db.Allocations.AnyAsync(a=>a.TransactionId==id&&a.OwnerId==owner&&a.IsActive,ct))return Results.Problem("This receipt is already split into expenses. Change them under More options.",statusCode:409);
            var year=KuTax.Core.FinancialYears.FinancialYear.FromDate(transaction.Date.Value).StartYear;
            var rule=(await db.LoadRulesAsync(ct)).Find(year,request.Category);
            if(rule is not null&&!request.Declared)return Results.Problem($"Confirm the conditions for {rule.Label}.",statusCode:400);
            if(rule is not null&&rule.ClaimTypes.Length>0&&!rule.ClaimTypes.Contains(request.ClaimType))return Results.Problem($"Choose the claim type for {rule.Label}.",statusCode:400);
            transaction.Version=Guid.NewGuid();
            var allocation=AddExpense(db,owner,Actor(principal),transaction,request.Category,request.WorkUsePercent,rule?.ClaimTypes.Length>0?request.ClaimType:null,rule,"Receipt");
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}
            var row=(await AnnualAssessment.Load(db,owner,year,ct)).Rows.Single(r=>r.AllocationId==allocation.Id);
            var approved=row.Status=="Candidate";
            if(approved){ClaimEndpoints.Approve(db,owner,Actor(principal),allocation,transaction,row,year,"Receipt");await db.SaveChangesAsync(ct);}
            await dbTransaction.CommitAsync(ct);
            return Results.Ok(new{approved,status=approved?"Approved":row.Status,row.DeductibleAmount,row.Reason,year});
        }).RequireAuthorization();
        var group=endpoints.MapGroup("/api/quick-review/{year:int}").RequireAuthorization();
        group.MapGet("",async(int year,ClaimsPrincipal principal,KuTaxDbContext db,KuTax.Infrastructure.Rules.RuleCheckRequests ruleChecks,CancellationToken ct)=>
        {
            if(year is <2000 or >2100)return Results.BadRequest();
            ruleChecks.Request(year);
            var owner=Owner(principal);var ruleSet=await db.LoadRulesAsync(ct);var (ready,checks)=await Load(db,owner,year,ruleSet,ct);
            var reviewed=(await db.TaxRuleReviews.AsNoTracking().Select(r=>r.RuleVersion).ToListAsync(ct)).ToHashSet();
            // Sensible defaults from the owner's own history: the work use and claim type they last approved per category.
            var history=await db.Allocations.AsNoTracking().Where(a=>a.OwnerId==owner&&a.ApprovedAt!=null)
                .Select(a=>new{a.Category,a.WorkUsePercent,a.ClaimType,a.ApprovedAt}).ToListAsync(ct);
            var defaults=history.GroupBy(a=>a.Category).ToDictionary(g=>g.Key,g=>g.MaxBy(a=>a.ApprovedAt)!);
            return Results.Ok(new{year,
                items=ready.Select(x=>new{x.Transaction.Id,x.Transaction.Version,x.Transaction.DocumentId,x.FileName,x.Transaction.Merchant,x.Transaction.Date,x.Transaction.Amount,x.Transaction.Gst,
                    x.Transaction.Status,x.Transaction.SuggestedCategory,x.Note}),
                needsCheck=checks.Count,checks=checks.Take(20),
                rules=ruleSet.ForYear(year).Select(r=>new{r.Category,r.Label,r.Section,r.PaperReference,r.Conditions,r.Declaration,r.ClaimTypes,
                    maxItemCost=Parameter(r,"maxItemCost"),workUseAbove=Parameter(r,"workUseAbove"),r.WholeAmountOnly,r.SourceUrl,r.Version,reviewed=reviewed.Contains(r.Version),
                    defaultWorkUse=r.WholeAmountOnly?100m:defaults.GetValueOrDefault(r.Category)?.WorkUsePercent??100m,
                    defaultClaimType=defaults.GetValueOrDefault(r.Category)?.ClaimType})});
        });
        // Fills in category suggestions for receipts read before KuTax suggested them. Only merchant names are sent.
        group.MapPost("/suggest",async(int year,ClaimsPrincipal principal,KuTaxDbContext db,IAiOptionsSource settings,ICategorySuggester suggester,ILoggerFactory logs,CancellationToken ct)=>
        {
            if(year is <2000 or >2100)return Results.BadRequest();
            var owner=Owner(principal);var options=await settings.GetAsync(ct);var notice=AiPrivacy.NoticeVersion(options);
            if(!AiPrivacy.Configured(options)||!await db.PrivacyPreferences.AnyAsync(p=>p.OwnerId==owner&&p.ExternalAiConsent&&p.NoticeVersion==notice,ct))
                return Results.Problem("Turn on AI reading and give your consent in Settings first.",statusCode:409);
            var (ready,_)=await Load(db,owner,year,await db.LoadRulesAsync(ct),ct);
            var missing=ready.Where(x=>x.Transaction.SuggestedCategory is null).Select(x=>x.Transaction.Id).ToHashSet();
            var transactions=await db.Transactions.Where(t=>t.OwnerId==owner&&missing.Contains(t.Id)).ToListAsync(ct);
            var merchants=transactions.Select(t=>t.Merchant!).Distinct(StringComparer.OrdinalIgnoreCase).Take(CategorySuggester.MaxMerchants).ToList();
            IReadOnlyDictionary<int,string>? found;
            try{found=await suggester.SuggestAsync(merchants,ct);}
            catch(Exception error) when(error is HttpRequestException or InvalidDataException or JsonException or ProviderRateLimitedException or TaskCanceledException)
            {
                logs.CreateLogger("QuickReview").LogWarning("Category suggestions unavailable ({ExceptionType}).",error.GetType().Name);
                return Results.Problem("Suggestions aren't available right now. You can still choose categories yourself.",statusCode:503);
            }
            var byMerchant=(found??new Dictionary<int,string>()).ToDictionary(p=>merchants[p.Key],p=>p.Value,(IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase);
            var suggested=0;
            foreach(var transaction in transactions)if(byMerchant.TryGetValue(transaction.Merchant!,out var category)){transaction.SuggestedCategory=category;suggested++;}
            await db.SaveChangesAsync(ct);
            return Results.Ok(new{suggested,asked=merchants.Count});
        });
        group.MapPost("/approve",async(int year,QuickApproval request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            if(year is <2000 or >2100||!request.Confirm||request.Items is not {Length:>0 and <=MaxBatch}||request.Items.DistinctBy(i=>i.TransactionId).Count()!=request.Items.Length)
                return Results.Problem($"Select 1–{MaxBatch} receipts and confirm.",statusCode:400);
            var declared=(request.Declarations??[]).ToHashSet();var ruleSet=await db.LoadRulesAsync(ct);
            foreach(var item in request.Items)
            {
                if(!ReceiptCandidate.SuggestableCategories.Contains(item.Category))return Results.Problem("Choose a category for every receipt.",statusCode:400);
                if(item.WorkUsePercent is <0 or >100||decimal.Round(item.WorkUsePercent,2)!=item.WorkUsePercent)return Results.Problem("Work use must be 0–100%.",statusCode:400);
                var rule=ruleSet.Find(year,item.Category);
                if(rule is null)continue;
                if(!declared.Contains(item.Category))return Results.Problem($"Confirm the conditions for {rule.Label}.",statusCode:400);
                if(rule.ClaimTypes.Length>0&&!rule.ClaimTypes.Contains(item.ClaimType))return Results.Problem($"Choose the claim type for {rule.Label}.",statusCode:400);
                if(rule.ClaimTypes.Length==0&&item.ClaimType is not null)return Results.BadRequest();
            }
            var owner=Owner(principal);
            await using var dbTransaction=await db.Database.BeginTransactionAsync(ct);
            var (ready,_)=await Load(db,owner,year,ruleSet,ct);var readyIds=ready.ToDictionary(x=>x.Transaction.Id);
            var ids=request.Items.Select(i=>i.TransactionId).ToArray();
            var transactions=await db.Transactions.Where(t=>t.OwnerId==owner&&ids.Contains(t.Id)).ToDictionaryAsync(t=>t.Id,ct);
            foreach(var item in request.Items)
                if(!readyIds.ContainsKey(item.TransactionId)||!transactions.TryGetValue(item.TransactionId,out var t)||t.Version!=item.Version)
                    return Results.Problem("Some receipts changed or need a closer look. Refresh and try again.",statusCode:409);
            var created=new List<(ExpenseAllocation Allocation,ReceiptTransaction Transaction)>();var skipped=0;
            foreach(var item in request.Items)
            {
                var transaction=transactions[item.TransactionId];
                if(item.Category=="Personal")
                {
                    // Not for the tax return: the same as "Skip — not for my tax return" in the one-by-one review, and reversible there.
                    transaction.Status="Dismissed";transaction.SkipReason="Personal";transaction.ConfirmedAt=null;transaction.Version=Guid.NewGuid();skipped++;
                    db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action="ReceiptNotClaimed",DetailsJson=JsonSerializer.Serialize(new{TransactionId=transaction.Id,via="QuickReview"},ReceiptCandidate.Json)});
                    continue;
                }
                if(transaction.Status!="Confirmed")
                {
                    transaction.Status="Confirmed";transaction.ConfirmedAt=DateTimeOffset.UtcNow;
                    db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),ResourceId=transaction.Id,Action="ReceiptConfirmed",DetailsJson=JsonSerializer.Serialize(new{after=transaction,via="QuickReview"},ReceiptCandidate.Json)});
                }
                transaction.Version=Guid.NewGuid();
                var allocation=AddExpense(db,owner,Actor(principal),transaction,item.Category,item.WorkUsePercent,item.ClaimType,ruleSet.Find(year,item.Category),"QuickReview");
                created.Add((allocation,transaction));
            }
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}
            // Approve what the verified rules assess as a candidate; everything else stays visible with its reason.
            var assessment=await AnnualAssessment.Load(db,owner,year,ct);var rows=assessment.Rows.ToDictionary(r=>r.AllocationId);
            var approved=new List<decimal>();var notApproved=new List<object>();var keptForAdvice=0;
            foreach(var (allocation,transaction) in created)
            {
                var row=rows[allocation.Id];
                if(row.Status=="Candidate"){ClaimEndpoints.Approve(db,owner,Actor(principal),allocation,transaction,row,year,"QuickReview");approved.Add(row.DeductibleAmount!.Value);}
                else if(allocation.Category=="Unsupported")keptForAdvice++;
                else notApproved.Add(new{transactionId=transaction.Id,transaction.Merchant,row.Status,row.Reason});
            }
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){return Results.Conflict();}
            await dbTransaction.CommitAsync(ct);
            return Results.Ok(new{approved=approved.Count,approvedTotal=approved.Sum(),skipped,keptForAdvice,notApproved});
        });
    }

    /// <summary>
    /// The receipt's single expense in one category, as quick review and the one-page receipt create it. Without a
    /// verified rule the expense is kept for advice with no deduction.
    /// </summary>
    internal static ExpenseAllocation AddExpense(KuTaxDbContext db,string owner,string actor,ReceiptTransaction transaction,string category,decimal workUse,string? claimType,TaxRule? rule,string via)
    {
        var allocation=new ExpenseAllocation{OwnerId=owner,TransactionId=transaction.Id,Description=transaction.Merchant!.Length>200?transaction.Merchant[..200]:transaction.Merchant,
            Category=rule is null?"Unsupported":category,ClaimType=claimType,Amount=transaction.Amount!.Value,
            WorkUsePercent=rule?.WholeAmountOnly==true?100:workUse,WorkReimbursement=0,
            Basis=rule is null?"Kept for advice: KuTax has no verified rule for this expense.":"Confirmed in quick review: "+rule.Declaration,
            EligibilityConfirmed=rule is not null,Status="Pending"};
        db.Allocations.Add(allocation);
        db.AuditEvents.Add(new(){OwnerId=owner,ActorId=actor,ResourceId=transaction.Id,Action="AllocationsReplaced",DetailsJson=JsonSerializer.Serialize(new{before=Array.Empty<object>(),after=new[]{allocation},via},ReceiptCandidate.Json)});
        return allocation;
    }

    private sealed record Ready(ReceiptTransaction Transaction,string FileName,string? Note);

    /// <summary>
    /// Receipts in the year that can safely be reviewed together: read by AI with confident merchant, date and amount,
    /// in AUD, not a possible duplicate, not processing, and not yet split into expenses. Returns them and the count of
    /// open receipts that still need the one-by-one review.
    /// </summary>
    private static decimal? Parameter(TaxRule rule,string name)=>RuleEngine.Definition(rule).Parameters.TryGetValue(name,out var value)?value:null;
    /// <summary>A receipt that needs the one-by-one review, and the plain reason why.</summary>
    private sealed record Check(Guid DocumentId,string? Merchant,decimal? Amount,string Reason);
    private static async Task<(List<Ready> Ready,List<Check> Checks)> Load(KuTaxDbContext db,string owner,int year,TaxRuleSet rules,CancellationToken ct)
    {
        var from=new DateOnly(year,7,1);var to=from.AddYears(1);
        var open=await db.Transactions.AsNoTracking().Where(t=>t.OwnerId==owner&&t.Status!="Dismissed"&&t.Date>=from&&t.Date<to
                &&!db.Allocations.Any(a=>a.TransactionId==t.Id&&a.IsActive))
            .Join(db.Documents.Where(d=>d.OwnerId==owner),t=>t.DocumentId,d=>d.Id,(t,d)=>new{Transaction=t,Document=d}).ToListAsync(ct);
        var documentIds=open.Select(x=>x.Document.Id).ToArray();
        var busy=(await db.ProcessingJobs.AsNoTracking().Where(j=>documentIds.Contains(j.DocumentId)&&(j.Status=="Queued"||j.Status=="Processing")).Select(j=>j.DocumentId).ToListAsync(ct)).ToHashSet();
        var attempts=(await db.ExtractionAttempts.AsNoTracking().Where(e=>e.OwnerId==owner&&documentIds.Contains(e.DocumentId)).ToListAsync(ct))
            .GroupBy(e=>e.DocumentId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(e=>e.CreatedAt).First());
        var keys=await db.Transactions.AsNoTracking().Where(t=>t.OwnerId==owner&&t.Status!="Dismissed"&&t.Date>=from.AddDays(-1)&&t.Date<to.AddDays(1))
            .Select(t=>new{t.Id,t.Merchant,t.Date,t.Amount,t.OriginalAmount}).ToListAsync(ct);
        var ready=new List<Ready>();var checks=new List<Check>();
        foreach(var x in open)
        {
            var t=x.Transaction;
            string? Why()
            {
                if(x.Document.Status=="Quarantined")return "Blocked by the malware check.";
                if(busy.Contains(x.Document.Id))return "Still being read.";
                if(x.Document.ScanStatus!="Clean")return "Waiting for the malware check.";
                if(t.Merchant is null||t.Amount is null)return t.OriginalCurrency is not null?$"In {t.OriginalCurrency}: enter the AUD amount.":"The merchant or amount is missing.";
                if(t.OriginalCurrency is not null&&t.ConversionSource is null)return $"In {t.OriginalCurrency}: confirm the AUD amount.";
                if(keys.Any(k=>k.Id!=t.Id&&k.Date==t.Date&&string.Equals(k.Merchant,t.Merchant,StringComparison.OrdinalIgnoreCase)
                    &&new[]{k.Amount,k.OriginalAmount}.Where(v=>v!=null).Intersect(new[]{t.Amount,t.OriginalAmount}.Where(v=>v!=null)).Any()))return "Looks like a duplicate of another receipt.";
                // A confirmed receipt has been checked by the person; a draft needs a confident AI reading of each value it still holds.
                if(t.Status=="Confirmed")return null;
                if(!attempts.TryGetValue(x.Document.Id,out var attempt)||attempt.Provider=="Manual")return "Typed in by hand: check and confirm it.";
                ReceiptCandidate candidate;
                try{candidate=JsonSerializer.Deserialize<ReceiptCandidate>(attempt.CandidateJson,ReceiptCandidate.Json)!;}catch(JsonException){return "The AI reading couldn't be used: check it.";}
                bool Confident<T>(ExtractedField<T> field,T? value)=>!Equals(field.Value,value)||field.Confidence>=MinConfidence;
                if(!Confident(candidate.Merchant,t.Merchant))return "AI wasn't sure of the merchant.";
                if(!Confident(candidate.Date,t.Date!.Value.ToString("yyyy-MM-dd")))return "AI wasn't sure of the date.";
                if(!Confident(candidate.Amount,t.Amount))return "AI wasn't sure of the amount.";
                return null;
            }
            if(Why() is {} reason){checks.Add(new(x.Document.Id,t.Merchant??x.Document.OriginalFileName,t.Amount,reason));continue;}
            // The lowest per-item limit among the year's rules (tools, software); dearer items need the one-by-one review.
            var limit=rules.ForYear(year).Select(r=>Parameter(r,"maxItemCost")).Where(v=>v is not null).Min();
            ready.Add(new(t,x.Document.OriginalFileName,limit is {} max&&t.Amount>max?$"Over ${max:0}: tools and software at this price decline in value, which KuTax doesn't calculate.":null));
        }
        // Receipts without a date belong to no year yet, so every year's list mentions them.
        var undated=await db.Transactions.AsNoTracking().Where(t=>t.OwnerId==owner&&t.Status!="Dismissed"&&t.Date==null)
            .Join(db.Documents.Where(d=>d.OwnerId==owner&&d.Purpose=="Receipt"),t=>t.DocumentId,d=>d.Id,(t,d)=>new{t.Merchant,t.Amount,d.Id,d.OriginalFileName}).Take(20).ToListAsync(ct);
        checks.AddRange(undated.Select(u=>new Check(u.Id,u.Merchant??u.OriginalFileName,u.Amount,"The date is missing, so its financial year isn't known.")));
        return (ready.OrderBy(r=>r.Transaction.Date).ThenBy(r=>r.Transaction.Merchant).ToList(),checks);
    }
    private static string Owner(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Owner(principal);
    private static string Actor(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Actor(principal);
}

public sealed record QuickItem(Guid TransactionId,Guid Version,string Category,decimal WorkUsePercent,string? ClaimType);
public sealed record QuickApproval(QuickItem[] Items,string[]? Declarations,bool Confirm);
public sealed record ClaimOne(Guid Version,string Category,decimal WorkUsePercent,string? ClaimType,bool Declared);
