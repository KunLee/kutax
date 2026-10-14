using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KuTax.IntegrationTests;
public sealed class SupportingDocumentIntegrationTests
{
    [RequiresServices]
    public async Task Supporting_documents_are_scanned_but_never_read_or_queued_and_attach_to_a_claim()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Processing:Enabled", "false"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>(); await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"support-{Guid.NewGuid():N}@kutax.test"; var owner = new ApplicationUser { UserName = email, Email = email };
        const string password = "Synthetic-Support-Test-481!";
        Assert.True((await users.CreateAsync(owner, password)).Succeeded);
        try
        {
            var batch = new ImportBatch { OwnerId = owner.Id, ExpectedFiles = 1, Status = "Complete" };
            var receipt = new ReceiptDocument { OwnerId = owner.Id, ImportBatchId = batch.Id, OriginalFileName = "phone-bill.pdf", RelativePath = "phone-bill.pdf", ObjectKey = "unused/" + Guid.NewGuid(), Sha256 = Guid.NewGuid().ToString("N"), ContentType = "application/pdf", ScanStatus = "Clean", Status = "NeedsReview" };
            var transaction = new ReceiptTransaction { OwnerId = owner.Id, DocumentId = receipt.Id, Merchant = "Synthetic Telco", Date = new(2025, 9, 1), Amount = 90m, Status = "Confirmed" };
            var allocation = new ExpenseAllocation { OwnerId = owner.Id, TransactionId = transaction.Id, Description = "Phone", Category = "PhoneInternet", Amount = 90m, WorkUsePercent = 40m, Basis = "x", EligibilityConfirmed = true };
            db.AddRange(batch, receipt, transaction, allocation);
            await db.SaveChangesAsync();

            using var client = ReceiptIntegrationTests.CreateClient(factory);
            await ReceiptIntegrationTests.Login(client, email, password);
            var created = await client.PostAsJsonAsync("/api/import-batches/", new { sourceKind = "Files", expectedFiles = 1, purpose = "Supporting" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var supportBatch = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/import-batches/", new { sourceKind = "Files", expectedFiles = 1, purpose = "Anything" })).StatusCode);
            var bytes = Encoding.UTF8.GetBytes($"%PDF-1.4\n% Synthetic 4-week phone diary {Guid.NewGuid():N}\n%%EOF");
            var stored = await ReceiptIntegrationTests.Upload(client, supportBatch, Guid.NewGuid(), "diary.pdf", bytes);
            var diary = stored.GetProperty("documentId").GetGuid();

            // It can be attached straight away, while its scan is still pending.
            var version = (await db.Allocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id)).Version;
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/allocations/{allocation.Id}/evidence", new { version, documents = new[] { diary }, note = "4-week diary" })).StatusCode);

            var worker = new ReceiptWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReceiptWorker>.Instance);
            for (var i = 0; i < 30 && (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == diary)).Status != "Supporting"; i++)
                if (!await worker.ProcessOneAsync(CancellationToken.None)) await Task.Delay(1000);
            var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == diary);
            Assert.Equal(("Supporting", "Clean", "Supporting"), (document.Status, document.ScanStatus, document.Purpose));
            Assert.False(await db.ExtractionAttempts.AnyAsync(e => e.DocumentId == diary)); // never sent to AI
            Assert.False(await db.Transactions.AnyAsync(t => t.DocumentId == diary));

            var receipts = await client.GetFromJsonAsync<JsonElement>("/api/documents/");
            Assert.DoesNotContain(receipts.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == diary);
            Assert.Equal(1, receipts.GetProperty("counts").GetProperty("total").GetInt32());
            var everything = await client.GetFromJsonAsync<JsonElement>("/api/documents/?kind=any");
            Assert.Contains(everything.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == diary);
            var year = await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/claims");
            Assert.Contains(year.GetProperty("rows")[0].GetProperty("evidence").EnumerateArray(), e => e.GetProperty("id").GetGuid() == diary);
            Assert.DoesNotContain(year.GetProperty("issues").EnumerateArray(), i => i.GetProperty("documentId").GetGuid() == diary); // not an "undated receipt"
        }
        finally
        {
            db.ChangeTracker.Clear();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            foreach (var document in await db.Documents.Where(d => d.OwnerId == owner.Id && d.ObjectKey.StartsWith("documents/")).ToListAsync()) await storage.DeleteAsync(document.ObjectKey, CancellationToken.None);
            await db.EvidenceLinks.Where(e => e.OwnerId == owner.Id).ExecuteDeleteAsync();
            await db.Allocations.Where(a => a.OwnerId == owner.Id).ExecuteDeleteAsync();
            await db.Transactions.Where(t => t.OwnerId == owner.Id).ExecuteDeleteAsync();
            await db.ProcessingJobs.Where(j => j.OwnerId == owner.Id).ExecuteDeleteAsync();
            await db.AuditEvents.Where(a => a.OwnerId == owner.Id).ExecuteDeleteAsync();
            await db.ImportItems.Where(i => db.ImportBatches.Any(b => b.Id == i.BatchId && b.OwnerId == owner.Id)).ExecuteDeleteAsync();
            await db.Documents.Where(d => d.OwnerId == owner.Id).ExecuteDeleteAsync();
            await db.ImportBatches.Where(b => b.OwnerId == owner.Id).ExecuteDeleteAsync();
            Assert.True((await users.DeleteAsync(owner)).Succeeded);
        }
    }
}
