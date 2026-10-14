using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Setup;
public static class RecoveryVerification
{
    public static async Task VerifyAsync(KuTaxDbContext db,IDocumentStorage storage,CancellationToken ct)
    {
        if((await db.Database.GetPendingMigrationsAsync(ct)).Any())throw new InvalidOperationException("Restore schema does not match this application version.");
        long bytes=0;var objects=0;
        async Task VerifyObject(string key,string expected,long size)
        {
            await using var content=await storage.OpenReadAsync(key,ct);
            if(content.Length!=size||!Convert.ToHexString(await SHA256.HashDataAsync(content.Content,ct)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Restored object integrity check failed.");
            bytes+=size;objects++;
        }
        foreach(var document in await db.Documents.AsNoTracking().ToListAsync(ct))await VerifyObject(document.ObjectKey,document.Sha256,document.Size);
        foreach(var job in await db.ExportJobs.AsNoTracking().Where(j=>j.Status=="Complete").ToListAsync(ct))foreach(var file in JsonSerializer.Deserialize<ExportFile[]>(job.FilesJson,ReceiptCandidate.Json)??[])await VerifyObject(file.ObjectKey,file.Sha256,file.Size);
        foreach(var snapshot in await db.ExportSnapshots.AsNoTracking().ToListAsync(ct))
        {
            if(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.SnapshotJson)))!=snapshot.Sha256)throw new InvalidDataException("Restored snapshot checksum mismatch.");
            var model=JsonSerializer.Deserialize<AnnualTaxSnapshot>(snapshot.SnapshotJson,ReceiptCandidate.Json)??throw new InvalidDataException("Invalid restored snapshot.");
            if(model.Assessment.Rows.Where(r=>r.Status=="Approved").Sum(r=>r.DeductibleAmount??0)!=model.Assessment.ApprovedTotal||decimal.Truncate(model.Assessment.ApprovedTotal)!=model.Assessment.EntryAmount)throw new InvalidDataException("Restored snapshot totals do not reconcile.");
            foreach(var evidence in model.Evidence)if(!await db.Documents.AnyAsync(d=>d.Id==evidence.Id&&d.OwnerId==snapshot.OwnerId&&d.Sha256==evidence.Sha256,ct))throw new InvalidDataException("Restored snapshot evidence reference missing.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new{verified=true,objects,bytes,users=await db.Users.CountAsync(ct),documents=await db.Documents.CountAsync(ct),transactions=await db.Transactions.CountAsync(ct),allocations=await db.Allocations.CountAsync(ct),claims=await db.TaxClaims.CountAsync(ct),snapshots=await db.ExportSnapshots.CountAsync(ct)},ReceiptCandidate.Json));
    }
}
