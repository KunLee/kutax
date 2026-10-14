using System.Security.Claims;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Receipts;
public static class ReviewEndpoints
{
    public static void MapReview(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api").RequireAuthorization();
        group.MapPost("/documents/{id:guid}/manual",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);if(!await db.Documents.AnyAsync(d=>d.Id==id&&d.OwnerId==owner&&d.ScanStatus=="Clean"&&d.Status!="Quarantined",ct))return Results.NotFound();
            if(await Reading(db,id,ct))return Results.Problem(StillReading,statusCode:409);
            var existing=await db.Transactions.SingleOrDefaultAsync(t=>t.DocumentId==id&&t.OwnerId==owner,ct);if(existing is not null)return Results.Ok(existing);
            var transaction=new ReceiptTransaction{OwnerId=owner,DocumentId=id};db.Transactions.Add(transaction);await db.SaveChangesAsync(ct);return Results.Ok(transaction);
        });
        group.MapGet("/documents/{id:guid}/review",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal); var document=await db.Documents.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id&&d.OwnerId==owner,ct);
            if(document is null)return Results.NotFound();
            var transaction=await db.Transactions.AsNoTracking().SingleOrDefaultAsync(t=>t.DocumentId==id&&t.OwnerId==owner,ct);
            var candidates=await db.ExtractionAttempts.AsNoTracking().Where(e=>e.DocumentId==id&&e.OwnerId==owner).OrderByDescending(e=>e.CreatedAt).ToListAsync(ct);
            var job=await db.ProcessingJobs.AsNoTracking().SingleOrDefaultAsync(j=>j.DocumentId==id&&j.OwnerId==owner,ct);
            var transactionId=transaction?.Id??Guid.Empty;
            var history=await db.AuditEvents.AsNoTracking().Where(e=>e.OwnerId==owner&&(e.ResourceId==id||e.ResourceId==transactionId)).OrderByDescending(e=>e.CreatedAt).Take(100).ToListAsync(ct);
            // Same merchant and date with a matching amount, in AUD or as printed on the receipt: catches one copy typed
            // in AUD and another read in USD. A copy already skipped no longer counts.
            var original=transaction?.OriginalAmount;
            var duplicate=transaction?.Date is {} date && transaction.Amount is {} amount && transaction.Merchant is {} merchant
                && await db.Transactions.AnyAsync(t=>t.OwnerId==owner&&t.Id!=transaction.Id&&t.Status!="Dismissed"&&t.Date==date&&t.Merchant!.ToUpper()==merchant.ToUpper()
                    &&(t.Amount==amount||t.OriginalAmount==amount||original!=null&&(t.Amount==original||t.OriginalAmount==original)),ct);
            return Results.Ok(new{document=new{document.Id,document.Status,document.ScanStatus,document.OriginalFileName,document.ContentType},transaction,candidates,job,history,possibleDuplicate=duplicate});
        });
        // Queues every unconfirmed receipt to be read again, e.g. after AI reading is switched on.
        group.MapPost("/documents/read-again",async(ClaimsPrincipal principal,KuTaxDbContext db,IAiOptionsSource settings,CancellationToken ct)=>
        {
            var owner=Owner(principal);var options=await settings.GetAsync(ct);var notice=AiPrivacy.NoticeVersion(options);
            if(!AiPrivacy.Configured(options)||!await db.PrivacyPreferences.AnyAsync(p=>p.OwnerId==owner&&p.ExternalAiConsent&&p.NoticeVersion==notice,ct))
                return Results.Problem("Turn on AI reading and give your consent in Settings first.",statusCode:409);
            var ids=await db.Documents.Where(d=>d.OwnerId==owner&&d.Purpose=="Receipt"&&d.Status!="Quarantined"&&d.ScanStatus=="Clean"
                    &&!db.Transactions.Any(t=>t.DocumentId==d.Id&&(t.Status=="Confirmed"||t.Status=="Dismissed"||t.Merchant!=null||t.Date!=null||t.Amount!=null||t.Notes!="")))
                .OrderBy(d=>d.CreatedAt).Select(d=>d.Id).Take(500).ToListAsync(ct);
            var now=DateTimeOffset.UtcNow;
            var queued=await db.ProcessingJobs.Where(j=>j.OwnerId==owner&&ids.Contains(j.DocumentId)&&j.Status!="Processing"&&j.Status!="Queued")
                .ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Queued").SetProperty(j=>j.Attempts,0).SetProperty(j=>j.AvailableAt,now)
                    .SetProperty(j=>j.LeaseToken,(Guid?)null).SetProperty(j=>j.ErrorCode,(string?)null),ct);
            await db.Documents.Where(d=>d.OwnerId==owner&&ids.Contains(d.Id)&&db.ProcessingJobs.Any(j=>j.DocumentId==d.Id&&j.Status=="Queued"))
                .ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,"Stored"),ct);
            return Results.Ok(new{queued});
        });
        // "Skip — not for my tax return": keeps the record but takes it out of the review queue, re-reading and annual exceptions.
        group.MapPut("/transactions/{id:guid}/dismiss",async(Guid id,DismissRequest request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var transaction=await db.Transactions.SingleOrDefaultAsync(t=>t.Id==id&&t.OwnerId==owner,ct);
            if(transaction is null)return Results.NotFound();
            if(transaction.Version!=request.Version)return Results.Problem("This receipt changed. Reload it and try again.",statusCode:409);
            if(await Reading(db,transaction.DocumentId,ct))return Results.Problem(StillReading,statusCode:409);
            if(request.Dismiss&&await db.Allocations.AnyAsync(a=>a.TransactionId==id&&a.OwnerId==owner&&a.IsActive,ct))
                return Results.Problem("Remove this receipt's expenses before skipping it.",statusCode:409);
            if(request.Reason is not null&&!ReceiptTransaction.SkipReasons.Contains(request.Reason))return Results.BadRequest();
            if(request.Dismiss==(transaction.Status=="Dismissed")&&(!request.Dismiss||request.Reason==transaction.SkipReason))return Results.Ok(transaction);
            transaction.Status=request.Dismiss?"Dismissed":"Draft";transaction.SkipReason=request.Dismiss?request.Reason:null;transaction.ConfirmedAt=null;transaction.Version=Guid.NewGuid();
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action=request.Dismiss?"ReceiptNotClaimed":"ReceiptRestored",DetailsJson=JsonSerializer.Serialize(new{TransactionId=id,request.Reason},ReceiptCandidate.Json)});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}
            return Results.Ok(transaction);
        });
        // Skip or restore many receipts at once. Each follows the single-receipt rules; the ones that can't change are
        // returned with the reason, and the rest are saved together.
        group.MapPost("/documents/skip-batch",async(SkipBatchRequest request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            if(request.DocumentIds is not {Length:>=1 and <=200}||request.DocumentIds.Distinct().Count()!=request.DocumentIds.Length)return Results.BadRequest();
            if(request.Reason is not null&&(!request.Skip||!ReceiptTransaction.SkipReasons.Contains(request.Reason)))return Results.BadRequest();
            var owner=Owner(principal);var ids=request.DocumentIds;
            var documents=await db.Documents.Where(d=>d.OwnerId==owner&&ids.Contains(d.Id)).ToDictionaryAsync(d=>d.Id,ct);
            var transactions=await db.Transactions.Where(t=>t.OwnerId==owner&&ids.Contains(t.DocumentId)).ToDictionaryAsync(t=>t.DocumentId,ct);
            var reading=(await db.ProcessingJobs.Where(j=>ids.Contains(j.DocumentId)&&(j.Status=="Queued"||j.Status=="Processing")).Select(j=>j.DocumentId).ToListAsync(ct)).ToHashSet();
            var withExpenses=(await db.Allocations.Where(a=>a.OwnerId==owner&&a.IsActive&&db.Transactions.Any(t=>t.Id==a.TransactionId&&ids.Contains(t.DocumentId)))
                .Select(a=>a.TransactionId).Distinct().ToListAsync(ct)).ToHashSet();
            var changed=0;var notChanged=new List<object>();
            foreach(var id in ids)
            {
                string? why=!documents.TryGetValue(id,out var document)?"Not found."
                    :document.Status=="Quarantined"||document.ScanStatus!="Clean"?"Blocked by the malware check."
                    :reading.Contains(id)?"Still being read."
                    :request.Skip&&transactions.TryGetValue(id,out var withAllocations)&&withExpenses.Contains(withAllocations.Id)?"It's split into expenses. Remove them in the receipt first."
                    :null;
                if(why is not null){notChanged.Add(new{documentId=id,reason=why});continue;}
                if(!transactions.TryGetValue(id,out var transaction))
                {
                    if(!request.Skip)continue;
                    transaction=new ReceiptTransaction{OwnerId=owner,DocumentId=id};db.Transactions.Add(transaction);
                }
                if(request.Skip==(transaction.Status=="Dismissed")&&(!request.Skip||request.Reason==transaction.SkipReason))continue;
                transaction.Status=request.Skip?"Dismissed":"Draft";transaction.SkipReason=request.Skip?request.Reason:null;transaction.ConfirmedAt=null;transaction.Version=Guid.NewGuid();
                db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action=request.Skip?"ReceiptNotClaimed":"ReceiptRestored",DetailsJson=JsonSerializer.Serialize(new{TransactionId=transaction.Id,request.Reason,Batch=true},ReceiptCandidate.Json)});
                changed++;
            }
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Problem("Some receipts changed. Refresh and try again.",statusCode:409);}
            return Results.Ok(new{changed,notChanged});
        });
        group.MapPost("/documents/{id:guid}/retry",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal); var document=await db.Documents.SingleOrDefaultAsync(d=>d.Id==id&&d.OwnerId==owner,ct);
            if(document is null)return Results.NotFound();
            if(document.Status=="Quarantined")return Results.Problem("This document is quarantined.",statusCode:409);
            var job=await db.ProcessingJobs.SingleOrDefaultAsync(j=>j.DocumentId==id,ct);
            if(job is null){job=new(){DocumentId=id,OwnerId=owner};db.ProcessingJobs.Add(job);}
            if(job.Status=="Processing")return Results.Conflict();
            job.Status="Queued";job.Attempts=0;job.AvailableAt=DateTimeOffset.UtcNow;job.LeaseToken=null;job.ErrorCode=null;document.Status="Stored";
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action="ProcessingRequested",ResourceId=id});await db.SaveChangesAsync(ct);return Results.Accepted();
        });
        // Stop waiting for AI on a receipt that hasn't started reading yet, so the person can enter it themselves.
        // A receipt already being read can't be taken over; that only takes a few seconds.
        group.MapPost("/documents/{id:guid}/take-over",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);if(!await db.Documents.AnyAsync(d=>d.Id==id&&d.OwnerId==owner,ct))return Results.NotFound();
            var stopped=await db.ProcessingJobs.Where(j=>j.DocumentId==id&&j.OwnerId==owner&&j.Status=="Queued")
                .ExecuteUpdateAsync(u=>u.SetProperty(j=>j.Status,"Cancelled").SetProperty(j=>j.ErrorCode,"TakenOver").SetProperty(j=>j.LeaseToken,(Guid?)null).SetProperty(j=>j.LeaseUntil,(DateTimeOffset?)null),ct);
            if(stopped==0)return await Reading(db,id,ct)?Results.Problem(StillReading,statusCode:409):Results.NoContent();
            await db.Documents.Where(d=>d.Id==id&&d.OwnerId==owner&&d.Status=="Stored").ExecuteUpdateAsync(u=>u.SetProperty(d=>d.Status,"NeedsReview").SetProperty(d=>d.Version,Guid.NewGuid()),ct);
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action="ProcessingTakenOver",ResourceId=id});await db.SaveChangesAsync(ct);return Results.NoContent();
        });
        // The RBA rate KuTax would use for a foreign-currency receipt on a given date (A$1 = X units).
        group.MapGet("/exchange-rates/{currency}/{date}",async(string currency,DateOnly date,decimal? amount,IExchangeRateSource rates,CancellationToken ct)=>
        {
            if(!ForeignCurrency.IsCode(currency)||date.Year<2000)return Results.BadRequest();
            var quote=await rates.FindAsync(currency,date,ct);
            return quote is null?Results.Problem($"No RBA rate is available for {currency} around {date:d MMM yyyy}. Use the AUD amount from your bank or card statement.",statusCode:404)
                :Results.Ok(new{quote.Currency,quote.RequestedDate,quote.RateDate,quote.UnitsPerAud,
                    // The AUD figure is computed here so the screen and the save check round identically.
                    aud=amount is > 0 and <= ForeignCurrency.MaxAmount?ForeignCurrency.ToAud(amount.Value,quote.UnitsPerAud):(decimal?)null,source="Reserve Bank of Australia, F11.1 daily exchange rates",sourceUrl=RbaExchangeRates.SourceUrl});
        });
        group.MapPut("/transactions/{id:guid}",async(Guid id,ReviewRequest request,ClaimsPrincipal principal,KuTaxDbContext db,IExchangeRateSource rates,CancellationToken ct)=>
        {
            var owner=Owner(principal);var transaction=await db.Transactions.SingleOrDefaultAsync(t=>t.Id==id&&t.OwnerId==owner,ct);
            if(transaction is null)return Results.NotFound();
            if(transaction.Version!=request.Version)return Results.Problem("This receipt changed. Reload it before saving.",statusCode:409);
            if(await Reading(db,transaction.DocumentId,ct))return Results.Problem(StillReading,statusCode:409);
            if(string.IsNullOrWhiteSpace(request.Merchant)||request.Merchant.Length>200||request.Date is null||request.Date.Value.Year<2000||request.Date.Value>DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
                ||request.Amount is null or <=0 or >1000000||decimal.Round(request.Amount.Value,2)!=request.Amount||request.Gst is <0||request.Gst>request.Amount||request.Gst.HasValue&&decimal.Round(request.Gst.Value,2)!=request.Gst||request.Notes?.Length>2000)
                return Results.Problem("Enter a merchant, valid receipt date and positive AUD amount with at most two decimal places. GST cannot exceed the amount.",statusCode:400);
            if(!await db.Documents.AnyAsync(d=>d.Id==transaction.DocumentId&&d.ScanStatus=="Clean"&&d.Status!="Quarantined",ct))return Results.Conflict();
            // Foreign-currency receipts: the AUD amount comes from a statement, or from the RBA rate checked here.
            ExchangeRateQuote? quote=null;
            if(request.Foreign is {} foreign)
            {
                if(!ForeignCurrency.IsCode(foreign.Currency)||foreign.Amount is not (> 0 and <= ForeignCurrency.MaxAmount)||decimal.Round(foreign.Amount,2)!=foreign.Amount||!ForeignCurrency.Sources.Contains(foreign.Source))
                    return Results.Problem("Enter the currency code, the amount shown on the receipt, and how you converted it.",statusCode:400);
                if(foreign.Source=="RBA")
                {
                    quote=await rates.FindAsync(foreign.Currency,request.Date!.Value,ct);
                    if(quote is null)return Results.Problem($"No RBA rate is available for {foreign.Currency} on that date. Use the AUD amount from your bank or card statement.",statusCode:409);
                    var expected=ForeignCurrency.ToAud(foreign.Amount,quote.UnitsPerAud);
                    if(request.Amount!=expected)return Results.Problem($"At the RBA rate for that date, {foreign.Currency} {foreign.Amount:0.00} is A${expected:0.00}. Recalculate and try again.",statusCode:409);
                }
            }
            var before=JsonSerializer.Serialize(transaction,ReceiptCandidate.Json);
            // Every fact edit requires fresh claim approval, even if the user re-confirms immediately.
            foreach(var allocation in await db.Allocations.Where(a=>a.TransactionId==id&&a.OwnerId==owner).ToListAsync(ct))
            { allocation.Status=allocation.Category=="Excluded"?"Excluded":"Pending";allocation.ApprovedAt=null;allocation.ApprovedBy=null;allocation.ApprovedRuleVersion=null;allocation.ApprovedTransactionVersion=null;allocation.Version=Guid.NewGuid(); }
            transaction.Merchant=request.Merchant.Trim();transaction.Date=request.Date;transaction.Amount=request.Amount;transaction.Gst=request.Gst;transaction.Notes=request.Notes??"";
            transaction.OriginalCurrency=request.Foreign?.Currency;transaction.OriginalAmount=request.Foreign?.Amount;transaction.ConversionSource=request.Foreign?.Source;
            transaction.ConversionRate=quote?.UnitsPerAud;transaction.ConversionRateDate=quote?.RateDate;
            transaction.Status=request.Confirm?"Confirmed":"Draft";transaction.ConfirmedAt=request.Confirm?DateTimeOffset.UtcNow:null;transaction.Version=Guid.NewGuid();
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),ResourceId=id,Action=request.Confirm?"ReceiptConfirmed":"ReceiptEdited",DetailsJson=JsonSerializer.Serialize(new{before=JsonDocument.Parse(before).RootElement,after=transaction},ReceiptCandidate.Json)});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}return Results.Ok(transaction);
        });
        group.MapGet("/privacy",async(ClaimsPrincipal principal,KuTaxDbContext db,IAiOptionsSource settings,CancellationToken ct)=>
        {
            var consent=await db.PrivacyPreferences.AsNoTracking().SingleOrDefaultAsync(p=>p.OwnerId==Owner(principal),ct);
            var options=await settings.GetAsync(ct);var noticeVersion=AiPrivacy.NoticeVersion(options);
            return Results.Ok(new{externalAiConsent=consent?.ExternalAiConsent==true&&consent.NoticeVersion==noticeVersion,noticeVersion,configured=AiPrivacy.Configured(options),provider=AiPrivacy.ProviderName(options),model=options.Model,textModel=AiPrivacy.IsGroq(options)?options.TextModel:null,region=options.Region,retentionNotice=options.RetentionNotice,privacyUrl=AiPrivacy.PrivacyUrl(options)});
        });
        group.MapPut("/privacy",async(PrivacyRequest request,ClaimsPrincipal principal,KuTaxDbContext db,IAiOptionsSource settings,CancellationToken ct)=>
        {
            var options=await settings.GetAsync(ct);
            if(request.NoticeVersion!=AiPrivacy.NoticeVersion(options)||request.ExternalAiConsent&&!AiPrivacy.Configured(options))return Results.Problem("The operator must configure and review the provider privacy settings first.",statusCode:400);
            var owner=Owner(principal);var preference=await db.PrivacyPreferences.FindAsync([owner],ct);
            if(preference is null){preference=new(){OwnerId=owner};db.PrivacyPreferences.Add(preference);}preference.ExternalAiConsent=request.ExternalAiConsent;preference.NoticeVersion=request.NoticeVersion;preference.UpdatedAt=DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action=request.ExternalAiConsent?"AiConsentGranted":"AiConsentRevoked",DetailsJson=JsonSerializer.Serialize(new{request.NoticeVersion},ReceiptCandidate.Json)});
            await db.SaveChangesAsync(ct);return Results.NoContent();
        });
    }
    private const string StillReading="KuTax is still reading this receipt. You can check it as soon as it's done.";
    /// <summary>AI and the person never edit a receipt at the same time: while it's queued or being read, it's AI's turn.</summary>
    private static Task<bool> Reading(KuTaxDbContext db,Guid documentId,CancellationToken ct)=>db.ProcessingJobs.AnyAsync(j=>j.DocumentId==documentId&&(j.Status=="Queued"||j.Status=="Processing"),ct);
    private static string Owner(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Owner(principal);
    private static string Actor(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Actor(principal);
}
public sealed record ReviewRequest(Guid Version,string Merchant,DateOnly? Date,decimal? Amount,decimal? Gst,string? Notes,bool Confirm,ForeignAmount? Foreign=null);
/// <summary>What a foreign-currency receipt shows, and whether its AUD amount came from a statement or the RBA rate.</summary>
public sealed record ForeignAmount(string Currency,decimal Amount,string Source);
public sealed record PrivacyRequest(bool ExternalAiConsent,string NoticeVersion);
public sealed record DismissRequest(Guid Version, bool Dismiss, string? Reason = null);
public sealed record SkipBatchRequest(Guid[] DocumentIds, bool Skip, string? Reason = null);
