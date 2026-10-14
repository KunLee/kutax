using System.Text.Json;
using System.Security.Cryptography;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace KuTax.Infrastructure.Processing;
public sealed class ReceiptWorker(IServiceScopeFactory scopes,ILogger<ReceiptWorker> logger) : BackgroundService
{
    // Waits longer than this mean an exhausted daily quota: hand the receipt to manual entry instead of queueing for hours.
    internal static readonly TimeSpan MaxRateLimitWait=TimeSpan.FromMinutes(10);
    private DateTimeOffset pausedUntil=DateTimeOffset.MinValue;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try { if(await ProcessOneAsync(stoppingToken)) continue; }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception) { logger.LogWarning("Receipt worker could not reach its queue; retrying."); }
            await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);
        }
    }
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        // The whole queue pauses while the provider is rate limiting, so other receipts don't burn their attempts.
        if(DateTimeOffset.UtcNow<pausedUntil) return false;
        using var scope=scopes.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        // A single statement claims the next row. Expired leases survive process crashes and multiple workers.
        var token=Guid.NewGuid(); var now=DateTimeOffset.UtcNow; var lease=now.AddMinutes(3);
        await db.ProcessingJobs.Where(j=>j.Status=="Processing"&&j.LeaseUntil<now&&j.Attempts>=3).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Failed").SetProperty(j=>j.ErrorCode,"RetryLimit"),ct);
        var claimed=await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProcessingJobs" SET "Status"='Processing', "LeaseUntil"={lease}, "LeaseToken"={token}, "Attempts"="Attempts"+1
            WHERE "Id"=(SELECT "Id" FROM "ProcessingJobs" WHERE "Attempts"<3 AND (("Status"='Queued' AND "AvailableAt"<={now}) OR ("Status"='Processing' AND "LeaseUntil"<{now})) ORDER BY "AvailableAt" FOR UPDATE SKIP LOCKED LIMIT 1)
            """,ct);
        if(claimed==0) return false;
        var job=await db.ProcessingJobs.SingleAsync(x=>x.LeaseToken==token,ct);
        var document=await db.Documents.SingleAsync(x=>x.Id==job.DocumentId,ct);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var storage=scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            if(document.ScanStatus!="Clean")
            {
                await using var scanContent=await storage.OpenReadAsync(document.ObjectKey,timeout.Token);
                var clean=await scope.ServiceProvider.GetRequiredService<IDocumentScanner>().IsCleanAsync(scanContent.Content,timeout.Token);
                document.ScanStatus=clean?"Clean":"Infected";document.ScannedAt=DateTimeOffset.UtcNow;
                if(!clean){document.Status="Quarantined";job.Status="Failed";job.ErrorCode="MalwareDetected";job.LeaseUntil=null;await db.SaveChangesAsync(ct);return true;}
                await db.SaveChangesAsync(ct);
            }
            await using(var verify=await storage.OpenReadAsync(document.ObjectKey,timeout.Token))
                if(verify.Length!=document.Size||!Convert.ToHexString(await SHA256.HashDataAsync(verify.Content,timeout.Token)).Equals(document.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Original integrity check failed.");
            if(document.Purpose=="Supporting")
            {
                document.Status="Supporting";document.Version=Guid.NewGuid();job.Status="Complete";job.ErrorCode=null;job.LeaseUntil=null;
                await db.SaveChangesAsync(ct);return true;
            }
            await using var stored=await scope.ServiceProvider.GetRequiredService<IDocumentStorage>().OpenReadAsync(document.ObjectKey,timeout.Token);
            var noticeVersion=AiPrivacy.NoticeVersion(await scope.ServiceProvider.GetRequiredService<IAiOptionsSource>().GetAsync(timeout.Token));
            var consent=await db.PrivacyPreferences.AnyAsync(p=>p.OwnerId==job.OwnerId&&p.ExternalAiConsent&&p.NoticeVersion==noticeVersion,ct);
            var result=await scope.ServiceProvider.GetRequiredService<IReceiptExtractor>().ExtractAsync(document,stored.Content,consent,timeout.Token);
            // Lock the lease for final persistence; a stale worker cannot overwrite a newer attempt.
            await using var transaction=await db.Database.BeginTransactionAsync(ct);
            var owned=await db.ProcessingJobs.FromSqlInterpolated($"SELECT * FROM \"ProcessingJobs\" WHERE \"Id\"={job.Id} AND \"LeaseToken\"={token} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if(owned is null) return true;
            db.ExtractionAttempts.Add(new(){DocumentId=document.Id,OwnerId=document.OwnerId,Provider=result.Provider,Model=result.Model,CandidateJson=JsonSerializer.Serialize(result.Candidate,ReceiptCandidate.Json)});
            DateOnly? suggestedDate=result.Candidate.Date.Value is {} date?DateOnly.ParseExact(date,"yyyy-MM-dd"):null;
            var existing=await db.Transactions.SingleOrDefaultAsync(t=>t.DocumentId==document.Id,ct);
            var originalCurrency=result.Candidate.OriginalCurrency?.Value;var suggested=result.Candidate.Category?.Value;var originalAmount=originalCurrency is null?null:result.Candidate.OriginalAmount?.Value;
            if(existing is null) db.Transactions.Add(new(){DocumentId=document.Id,OwnerId=document.OwnerId,
                Merchant=result.Candidate.Merchant.Value,Date=suggestedDate,Amount=result.Candidate.Amount.Value,Gst=result.Candidate.Gst.Value,
                OriginalCurrency=originalCurrency,OriginalAmount=originalAmount,SuggestedCategory=suggested});
            // A re-read may fill a draft nobody has touched yet; anything a person typed or confirmed is never replaced.
            else if(existing.Status=="Draft"&&existing.Merchant is null&&existing.Date is null&&existing.Amount is null&&existing.Gst is null&&existing.Notes.Length==0
                &&result.Provider!="Manual")
            {existing.Merchant=result.Candidate.Merchant.Value;existing.Date=suggestedDate;existing.Amount=result.Candidate.Amount.Value;existing.Gst=result.Candidate.Gst.Value;
                existing.OriginalCurrency=originalCurrency;existing.OriginalAmount=originalAmount;existing.SuggestedCategory=suggested;existing.Version=Guid.NewGuid();}
            // A suggestion is never a fact: it can be refreshed on any re-read without touching what the person entered.
            else if(existing is not null&&suggested is not null&&existing.SuggestedCategory is null)existing.SuggestedCategory=suggested;
            document.Status="NeedsReview"; document.Version=Guid.NewGuid(); job.Status="Complete"; job.ErrorCode=null; job.LeaseUntil=null;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(ProviderRateLimitedException limited) when(limited.RetryAfter<=MaxRateLimitWait)
        {
            db.ChangeTracker.Clear();
            var wait=limited.RetryAfter<TimeSpan.FromSeconds(5)?TimeSpan.FromSeconds(5):limited.RetryAfter;
            pausedUntil=DateTimeOffset.UtcNow+wait;
            // Requeue without consuming an attempt; the receipt itself is fine.
            await db.ProcessingJobs.Where(j=>j.Id==job.Id&&j.LeaseToken==token).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Queued")
                .SetProperty(j=>j.Attempts,j=>j.Attempts-1).SetProperty(j=>j.ErrorCode,"RateLimited").SetProperty(j=>j.AvailableAt,pausedUntil).SetProperty(j=>j.LeaseUntil,(DateTimeOffset?)null),ct);
            logger.LogInformation("AI provider rate limited; receipt job {JobId} requeued after {Seconds}s.",job.Id,(int)wait.TotalSeconds);
        }
        catch(Exception ex)
        {
            db.ChangeTracker.Clear();
            var error=ex is InvalidDataException or JsonException or FormatException ? "InvalidExtraction" : ex is ProviderRateLimitedException ? "RateLimited" : "ProcessingUnavailable";
            var status=job.Attempts>=3 || error is "InvalidExtraction" or "RateLimited" ? "Failed":"Queued";
            var updated=await db.ProcessingJobs.Where(j=>j.Id==job.Id&&j.LeaseToken==token).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,status)
                .SetProperty(j=>j.ErrorCode,error).SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddSeconds(5*job.Attempts)).SetProperty(j=>j.LeaseUntil,(DateTimeOffset?)null),ct);
            if(status=="Failed"&&updated>0) await db.Documents.Where(d=>d.Id==document.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,"ProcessingFailed"),ct);
            logger.LogWarning("Receipt job {JobId} attempt {Attempt} ended with {Code} ({ExceptionType}).",job.Id,job.Attempts,error,ex.GetType().Name);
        }
        return true;
    }
}
