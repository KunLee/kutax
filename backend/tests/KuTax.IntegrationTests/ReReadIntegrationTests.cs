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
public sealed class ReReadIntegrationTests
{
    private sealed class SwitchableExtractor : IReceiptExtractor
    {
        public static bool AiOn { get; set; }
        public Task<(ReceiptCandidate Candidate,string Provider,string Model)> ExtractAsync(ReceiptDocument document,Stream content,bool consent,CancellationToken ct)=>
            Task.FromResult(AiOn&&consent
                ? (new ReceiptCandidate(new("Synthetic Reread Union",.97m,"SYNTHETIC REREAD UNION"),new("2025-08-01",.95m,"1/8/2025"),new(42.50m,.99m,"TOTAL 42.50"),new(null,0m,null),"AUD"),"Groq","synthetic/text")
                : (ReceiptCandidate.Empty,"Manual","manual-v1"));
    }

    [RequiresIsolatedServices]
    public async Task Reading_again_fills_untouched_manual_drafts_and_never_replaces_typed_details()
    {
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Processing:Enabled","false")
            .ConfigureTestServices(services=>{services.RemoveAll<IReceiptExtractor>();services.AddScoped<IReceiptExtractor,SwitchableExtractor>();}));
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();await db.Database.MigrateAsync();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email=$"reread-{Guid.NewGuid():N}@kutax.test";var owner=new ApplicationUser{UserName=email,Email=email};const string password="Synthetic-Reread-481!";
        Assert.True((await users.CreateAsync(owner,password)).Succeeded);
        // The workspace may have real AI settings: keep an exact copy and put it back afterwards.
        var original=await db.AiSettings.AsNoTracking().SingleOrDefaultAsync();
        try
        {
            SwitchableExtractor.AiOn=false;
            using var client=ReceiptIntegrationTests.CreateClient(factory);await ReceiptIntegrationTests.Login(client,email,password);
            var batch=(await (await client.PostAsJsonAsync("/api/import-batches/",new{sourceKind="Files",expectedFiles=2})).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Guid Upload(string name)=>ReceiptIntegrationTests.Upload(client,batch,Guid.NewGuid(),name,Encoding.UTF8.GetBytes($"%PDF-1.4\n% Synthetic {Guid.NewGuid():N}\n%%EOF")).Result.GetProperty("documentId").GetGuid();
            var untouched=Upload("untouched.pdf");var typed=Upload("typed.pdf");
            var worker=new ReceiptWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ReceiptWorker>.Instance);
            await db.ProcessingJobs.Where(j=>j.OwnerId==owner.Id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddYears(-1)));
            await DrainAsync(db,worker,owner.Id);
            // The person starts typing one receipt by hand.
            await db.Transactions.Where(t=>t.DocumentId==typed).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Merchant,"Typed by person"));

            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsync("/api/documents/read-again",null)).StatusCode); // AI not ready yet
            await scope.ServiceProvider.GetRequiredService<AiSettingsStore>().SaveAsync(new AiSettingsUpdate(true,"Groq","synthetic-key",false,"synthetic/vision","synthetic/text","Synthetic region","Synthetic notice"),owner.Id,CancellationToken.None);
            var privacy=await client.GetFromJsonAsync<JsonElement>("/api/privacy");
            Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/privacy",new{externalAiConsent=true,noticeVersion=privacy.GetProperty("noticeVersion").GetString()})).StatusCode);
            SwitchableExtractor.AiOn=true;

            var reread=await client.PostAsync("/api/documents/read-again",null);
            Assert.Equal(HttpStatusCode.OK,reread.StatusCode);
            Assert.Equal(1,(await reread.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("queued").GetInt32());
            await db.ProcessingJobs.Where(j=>j.OwnerId==owner.Id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.AvailableAt,DateTimeOffset.UtcNow.AddYears(-1)));
            await DrainAsync(db,worker,owner.Id);

            var filled=await db.Transactions.AsNoTracking().SingleAsync(t=>t.DocumentId==untouched);
            Assert.Equal(("Synthetic Reread Union",42.50m,"Draft"),(filled.Merchant,filled.Amount!.Value,filled.Status));
            Assert.Equal("Typed by person",(await db.Transactions.AsNoTracking().SingleAsync(t=>t.DocumentId==typed)).Merchant);
        }
        finally
        {
            SwitchableExtractor.AiOn=false;
            db.ChangeTracker.Clear();
            await db.AiSettings.ExecuteDeleteAsync();
            if(original is not null){db.AiSettings.Add(original);await db.SaveChangesAsync();db.ChangeTracker.Clear();}
            var storage=scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            foreach(var document in await db.Documents.Where(d=>d.OwnerId==owner.Id).ToListAsync())await storage.DeleteAsync(document.ObjectKey,CancellationToken.None);
            await db.ProcessingJobs.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ExtractionAttempts.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.AuditEvents.Where(j=>j.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.PrivacyPreferences.Where(p=>p.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.Transactions.Where(t=>t.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ImportItems.Where(i=>db.ImportBatches.Any(b=>b.Id==i.BatchId&&b.OwnerId==owner.Id)).ExecuteDeleteAsync();
            await db.Documents.Where(d=>d.OwnerId==owner.Id).ExecuteDeleteAsync();await db.ImportBatches.Where(b=>b.OwnerId==owner.Id).ExecuteDeleteAsync();
            Assert.True((await users.DeleteAsync(owner)).Succeeded);
        }
    }

    /// <summary>Processes this owner's queued jobs. A running app on the same database may take some first, so
    /// this waits until none are queued or being read, whoever handles them.</summary>
    private static async Task DrainAsync(KuTaxDbContext db,ReceiptWorker worker,string owner)
    {
        for(var i=0;i<300&&await db.ProcessingJobs.AnyAsync(j=>j.OwnerId==owner&&(j.Status=="Queued"||j.Status=="Processing"));i++)
            if(!await worker.ProcessOneAsync(CancellationToken.None))await Task.Delay(200);
        Assert.False(await db.ProcessingJobs.AnyAsync(j=>j.OwnerId==owner&&(j.Status=="Queued"||j.Status=="Processing")));
    }
}
