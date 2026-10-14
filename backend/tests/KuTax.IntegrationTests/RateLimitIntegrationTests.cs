using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace KuTax.IntegrationTests;
public sealed class RateLimitIntegrationTests
{
    private sealed class RateLimitedExtractor : IReceiptExtractor
    {
        public static TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(30);
        public Task<(ReceiptCandidate Candidate,string Provider,string Model)> ExtractAsync(ReceiptDocument document,Stream content,bool consent,CancellationToken ct)=>
            throw new ProviderRateLimitedException(Wait);
    }

    [RequiresIsolatedServices]
    public async Task Rate_limited_receipts_are_requeued_without_losing_attempts_and_daily_quota_falls_back_to_manual()
    {
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Processing:Enabled","false")
            .ConfigureTestServices(services=>{services.RemoveAll<IReceiptExtractor>();services.AddScoped<IReceiptExtractor,RateLimitedExtractor>();}));
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();await db.Database.MigrateAsync();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email=$"ratelimit-{Guid.NewGuid():N}@kutax.test";var owner=new ApplicationUser{UserName=email,Email=email};const string password="Synthetic-RateLimit-481!";
        Assert.True((await users.CreateAsync(owner,password)).Succeeded);
        try
        {
            using var client=ReceiptIntegrationTests.CreateClient(factory);await ReceiptIntegrationTests.Login(client,email,password);
            var batch=(await (await client.PostAsJsonAsync("/api/import-batches/",new{sourceKind="Files",expectedFiles=1})).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var stored=await ReceiptIntegrationTests.Upload(client,batch,Guid.NewGuid(),"limited.pdf",Encoding.UTF8.GetBytes($"%PDF-1.4\n% Synthetic {Guid.NewGuid():N}\n%%EOF"));
            var documentId=stored.GetProperty("documentId").GetGuid();
            await db.ProcessingJobs.Where(j=>j.DocumentId==documentId).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddYears(-1)));

            var worker=new ReceiptWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ReceiptWorker>.Instance);
            Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
            var job=await db.ProcessingJobs.AsNoTracking().SingleAsync(j=>j.DocumentId==documentId);
            Assert.Equal(("Queued",0,"RateLimited"),(job.Status,job.Attempts,job.ErrorCode));
            Assert.True(job.AvailableAt>DateTimeOffset.UtcNow.AddSeconds(20));
            Assert.False(await worker.ProcessOneAsync(CancellationToken.None)); // the queue is paused while limited

            // A wait longer than the cap means a spent daily quota: hand over to manual entry now.
            RateLimitedExtractor.Wait=TimeSpan.FromHours(3);
            await db.ProcessingJobs.Where(j=>j.DocumentId==documentId).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddYears(-1)));
            var fresh=new ReceiptWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ReceiptWorker>.Instance);
            Assert.True(await fresh.ProcessOneAsync(CancellationToken.None));
            job=await db.ProcessingJobs.AsNoTracking().SingleAsync(j=>j.DocumentId==documentId);
            Assert.Equal(("Failed","RateLimited"),(job.Status,job.ErrorCode));
            Assert.Equal("ProcessingFailed",(await db.Documents.AsNoTracking().SingleAsync(d=>d.Id==documentId)).Status);
        }
        finally
        {
            RateLimitedExtractor.Wait=TimeSpan.FromSeconds(30);
            db.ChangeTracker.Clear();
            var storage=scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            foreach(var document in await db.Documents.Where(d=>d.OwnerId==owner.Id).ToListAsync())await storage.DeleteAsync(document.ObjectKey,CancellationToken.None);
            await db.ProcessingJobs.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.AuditEvents.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ImportItems.Where(i=>db.ImportBatches.Any(b=>b.Id==i.BatchId&&b.OwnerId==owner.Id)).ExecuteDeleteAsync();
            await db.Documents.Where(d=>d.OwnerId==owner.Id).ExecuteDeleteAsync();await db.ImportBatches.Where(b=>b.OwnerId==owner.Id).ExecuteDeleteAsync();
            Assert.True((await users.DeleteAsync(owner)).Succeeded);
        }
    }
}
