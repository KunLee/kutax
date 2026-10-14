using System.Net;
using System.Security.Cryptography;
using System.Text;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using KuTax.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KuTax.IntegrationTests;
public sealed class ScanningIntegrationTests
{
    [RequiresIsolatedServices]
    public async Task Clamav_quarantines_harmless_eicar_fixture_and_scanner_outage_never_returns_clean()
    {
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();await db.Database.MigrateAsync();
        var scanner=scope.ServiceProvider.GetRequiredService<IDocumentScanner>();
        using(var clean=new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4\n% Synthetic clean fixture\n%%EOF")))Assert.True(await scanner.IsCleanAsync(clean,CancellationToken.None));
        // Standard harmless antivirus test signature, assembled only in memory.
        var signature="X5O!P%@AP[4\\PZX54(P^)7CC)7}"+"$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var bytes=Encoding.ASCII.GetBytes(signature);using(var sample=new MemoryStream(bytes))Assert.False(await scanner.IsCleanAsync(sample,CancellationToken.None));
        var unavailable=new ClamAvScanner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Scanning:Host","127.0.0.1"},{"Scanning:Port","1"}}).Build());
        using(var sample=new MemoryStream(bytes))await Assert.ThrowsAnyAsync<Exception>(()=>unavailable.IsCleanAsync(sample,CancellationToken.None));
        var email=$"scan-{Guid.NewGuid():N}@kutax.test";var owner=new ApplicationUser{UserName=email,Email=email};var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();Assert.True((await users.CreateAsync(owner,"Synthetic-Scanner-481!")).Succeeded);
        var batch=new ImportBatch{OwnerId=owner.Id,ExpectedFiles=1};var document=new ReceiptDocument{OwnerId=owner.Id,ImportBatchId=batch.Id,ObjectKey=$"scanner-fixtures/{Guid.NewGuid():N}",OriginalFileName="synthetic-antivirus-test.pdf",RelativePath="synthetic-antivirus-test.pdf",ContentType="application/pdf",Size=bytes.Length,Sha256=Convert.ToHexString(SHA256.HashData(bytes))};
        var storage=scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        try
        {
            using(var sample=new MemoryStream(bytes))await storage.PutAsync(document.ObjectKey,sample,"application/pdf",CancellationToken.None);
            db.ImportBatches.Add(batch);db.Documents.Add(document);db.ProcessingJobs.Add(new(){OwnerId=owner.Id,DocumentId=document.Id,AvailableAt=DateTimeOffset.UtcNow.AddYears(-5)});await db.SaveChangesAsync();
            var worker=new ReceiptWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ReceiptWorker>.Instance);Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
            var stored=await db.Documents.AsNoTracking().SingleAsync(d=>d.Id==document.Id);Assert.Equal("Infected",stored.ScanStatus);Assert.Equal("Quarantined",stored.Status);
            Assert.False(await db.ExtractionAttempts.AnyAsync(e=>e.DocumentId==document.Id));
            using var client=factory.CreateClient();await ReceiptIntegrationTests.Login(client,email,"Synthetic-Scanner-481!");Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync($"/api/documents/{document.Id}/content")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsync($"/api/documents/{document.Id}/retry",new StringContent("{}",Encoding.UTF8,"application/json"))).StatusCode);
        }
        finally
        {
            await storage.DeleteAsync(document.ObjectKey,CancellationToken.None);await db.ProcessingJobs.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();await db.Documents.Where(d=>d.OwnerId==owner.Id).ExecuteDeleteAsync();await db.ImportBatches.Where(b=>b.OwnerId==owner.Id).ExecuteDeleteAsync();db.ChangeTracker.Clear();Assert.True((await users.DeleteAsync(owner)).Succeeded);
        }
    }
}

