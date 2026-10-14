using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KuTax.Infrastructure.Reports;
public sealed class ExportWorker(IServiceScopeFactory scopes,ILogger<ExportWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{if(await ProcessOneAsync(stoppingToken))continue;}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Export worker could not reach its queue; retrying.");}
            await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);
        }
    }
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();var token=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        // Exhausted crashed leases are terminal until the owner explicitly retries.
        await db.ExportJobs.Where(j=>j.Status=="Processing"&&j.LeaseUntil<now&&j.Attempts>=3).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Failed").SetProperty(j=>j.ErrorCode,"RetryLimit"),ct);
        var claimed=await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ExportJobs" SET "Status"='Processing', "LeaseToken"={token}, "LeaseUntil"={now.AddMinutes(15)}, "Attempts"="Attempts"+1
            WHERE "Id"=(SELECT "Id" FROM "ExportJobs" WHERE "Attempts"<3 AND (("Status"='Queued' AND "AvailableAt"<={now}) OR ("Status"='Processing' AND "LeaseUntil"<{now})) ORDER BY "AvailableAt" FOR UPDATE SKIP LOCKED LIMIT 1)
            """,ct);
        if(claimed==0)return false;
        var job=await db.ExportJobs.AsNoTracking().SingleAsync(j=>j.LeaseToken==token,ct);ExportFile[] files=[];
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var snapshot=await db.ExportSnapshots.AsNoTracking().SingleAsync(s=>s.Id==job.Id&&s.OwnerId==job.OwnerId,timeout.Token);
            if(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.SnapshotJson)))!=snapshot.Sha256)throw new InvalidDataException("Snapshot checksum mismatch.");
            var model=JsonSerializer.Deserialize<AnnualTaxSnapshot>(snapshot.SnapshotJson,ReceiptCandidate.Json)??throw new InvalidDataException("Missing snapshot.");
            var documentIds=model.Evidence.Select(e=>e.Id).ToArray();
            if(await db.Documents.AnyAsync(d=>d.OwnerId==job.OwnerId&&documentIds.Contains(d.Id)&&(d.ScanStatus!="Clean"||d.Status=="Quarantined"),ct))throw new InvalidDataException("Evidence not cleared for use.");
            files=await scope.ServiceProvider.GetRequiredService<IAnnualReportGenerator>().GenerateAsync(model,snapshot.Sha256,timeout.Token);
            var json=JsonSerializer.Serialize(files,ReceiptCandidate.Json);
            var saved=await db.ExportJobs.Where(j=>j.Id==job.Id&&j.LeaseToken==token).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Complete").SetProperty(j=>j.FilesJson,json).SetProperty(j=>j.ErrorCode,(string?)null).SetProperty(j=>j.LeaseUntil,(DateTimeOffset?)null),ct);
            if(saved==0)await Cleanup(scope,files);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){await Cleanup(scope,files);throw;}
        catch(Exception error)
        {
            await Cleanup(scope,files);var code=error is InvalidDataException?"EvidenceIntegrity":"ExportUnavailable";var status=job.Attempts>=3||code=="EvidenceIntegrity"?"Failed":"Queued";
            await db.ExportJobs.Where(j=>j.Id==job.Id&&j.LeaseToken==token).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,status).SetProperty(j=>j.ErrorCode,code).SetProperty(j=>j.LeaseUntil,(DateTimeOffset?)null).SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddSeconds(10*job.Attempts)),ct);
            logger.LogWarning("Export job {Id} attempt {Attempt} ended with {Code} ({ExceptionType}).",job.Id,job.Attempts,code,error.GetType().Name);
        }
        return true;
    }
    private static async Task Cleanup(IServiceScope scope,ExportFile[] files){foreach(var file in files)try{await scope.ServiceProvider.GetRequiredService<IDocumentStorage>().DeleteAsync(file.ObjectKey,CancellationToken.None);}catch{/* Operator orphan reconciliation covers failed cleanup. */}}
}
