using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;
public sealed class SkipAndDeleteTests
{
    private const string Password = "Synthetic-Skip-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> Login(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }

    private static (ReceiptDocument Document, ReceiptTransaction Transaction) Seed(KuTaxDbContext db, string owner, string name)
    {
        var batch = new ImportBatch { OwnerId = owner, ExpectedFiles = 1 };
        var document = new ReceiptDocument { OwnerId = owner, ImportBatchId = batch.Id, OriginalFileName = name, RelativePath = name, ObjectKey = "k-" + Guid.NewGuid(), Sha256 = Guid.NewGuid().ToString("N"), ContentType = "application/pdf", ScanStatus = "Clean", Status = "NeedsReview" };
        var transaction = new ReceiptTransaction { DocumentId = document.Id, OwnerId = owner, Merchant = name, Date = new(2025, 9, 1), Amount = 20m };
        db.AddRange(batch, document, transaction,
            new ExtractionAttempt { DocumentId = document.Id, OwnerId = owner, Provider = "Groq", Model = "m" },
            new ProcessingJob { DocumentId = document.Id, OwnerId = owner, Status = "Complete" });
        return (document, transaction);
    }

    [Fact]
    public async Task Skipped_receipts_keep_a_reason_and_undo_restores_them()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        ReceiptTransaction transaction;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            (_, transaction) = Seed(db, owner.Id, "Groceries");
            Seed(db, owner.Id, "Course");
            await db.SaveChangesAsync(Ct);
        }
        using var client = await Login(factory, "owner@kutax.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/transactions/{transaction.Id}/dismiss", new { version = transaction.Version, dismiss = true, reason = "Bored" }, Ct)).StatusCode);
        var skipped = await client.PutAsJsonAsync($"/api/transactions/{transaction.Id}/dismiss", new { version = transaction.Version, dismiss = true, reason = "Personal" }, Ct);
        var body = await skipped.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("Dismissed", "Personal"), (body.GetProperty("status").GetString(), body.GetProperty("skipReason").GetString()));

        // (The skipped list itself is covered against PostgreSQL in DocumentListIntegrationTests.)
        // Undo restores it to the queue and forgets the reason.
        var restored = await client.PutAsJsonAsync($"/api/transactions/{transaction.Id}/dismiss", new { version = body.GetProperty("version").GetGuid(), dismiss = false }, Ct);
        var back = await restored.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("Draft", JsonValueKind.Null), (back.GetProperty("status").GetString(), back.GetProperty("skipReason").ValueKind));
    }

    [Fact]
    public async Task Mistaken_uploads_can_be_deleted_unless_they_are_part_of_a_claim_or_tax_pack()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        await factory.CreateUserAsync("stranger@kutax.test", Password);
        ReceiptDocument mistake, claimed, packed;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            (mistake, var mistakeTransaction) = Seed(db, owner.Id, "Wrong file");
            (claimed, var claimedTransaction) = Seed(db, owner.Id, "Approved fee");
            (packed, _) = Seed(db, owner.Id, "Frozen evidence");
            db.Allocations.Add(new ExpenseAllocation { OwnerId = owner.Id, TransactionId = mistakeTransaction.Id, Description = "x", Amount = 20m, WorkUsePercent = 100, Basis = "x" });
            var allocation = new ExpenseAllocation { OwnerId = owner.Id, TransactionId = claimedTransaction.Id, Description = "Fee", Category = "UnionFees", Amount = 20m, WorkUsePercent = 100, Basis = "x" };
            db.Allocations.Add(allocation);
            db.TaxClaims.Add(new TaxClaim { OwnerId = owner.Id, AllocationId = allocation.Id, FinancialYearStart = 2025, RuleVersion = "AU-2025-UnionFees-v1", DeductibleAmount = 20m, ActorId = owner.Id });
            db.ExportSnapshots.Add(new ExportSnapshot { Id = Guid.NewGuid(), OwnerId = owner.Id, FinancialYearStart = 2025, ApprovedBy = owner.Id, SnapshotJson = $"{{\"evidence\":[{{\"id\":\"{packed.Id}\"}}]}}", Sha256 = "x" });
            await db.SaveChangesAsync(Ct);
        }
        using var client = await Login(factory, "owner@kutax.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/documents/{mistake.Id}/delete", new { confirm = false }, Ct)).StatusCode);
        using (var stranger = await Login(factory, "stranger@kutax.test"))
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/documents/{mistake.Id}/delete", new { confirm = true }, Ct)).StatusCode);
        var blockedClaim = await client.PostAsJsonAsync($"/api/documents/{claimed.Id}/delete", new { confirm = true }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, blockedClaim.StatusCode);
        Assert.Contains("claim was approved", await blockedClaim.Content.ReadAsStringAsync(Ct));
        var blockedPack = await client.PostAsJsonAsync($"/api/documents/{packed.Id}/delete", new { confirm = true }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, blockedPack.StatusCode);
        Assert.Contains("tax pack", await blockedPack.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/documents/{mistake.Id}/delete", new { confirm = true }, Ct)).StatusCode);
        using var check = factory.Services.CreateScope();
        var verify = check.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        Assert.False(await verify.Documents.AnyAsync(d => d.Id == mistake.Id, Ct));
        Assert.False(await verify.Transactions.AnyAsync(t => t.DocumentId == mistake.Id, Ct));
        Assert.False(await verify.ExtractionAttempts.AnyAsync(e => e.DocumentId == mistake.Id, Ct));
        Assert.Equal(1, await verify.Allocations.CountAsync(Ct)); // only the claimed receipt's allocation is left
        var audit = await verify.AuditEvents.SingleAsync(a => a.Action == "DocumentDeleted", Ct);
        Assert.Contains("Wrong file", audit.DetailsJson);
        Assert.True(await verify.Documents.AnyAsync(d => d.Id == claimed.Id, Ct));
    }

    [Fact]
    public async Task Prefill_uses_the_suggested_category_and_the_owners_last_choices()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        await factory.CreateUserAsync("stranger@kutax.test", Password);
        ReceiptTransaction fresh, personal;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            (_, var earlier) = Seed(db, owner.Id, "Cursor");
            db.Allocations.Add(new ExpenseAllocation { OwnerId = owner.Id, TransactionId = earlier.Id, Description = "Cursor", Category = "SoftwareSubscriptions", Amount = 20m, WorkUsePercent = 80m, Basis = "x", ApprovedAt = DateTimeOffset.UtcNow });
            (_, fresh) = Seed(db, owner.Id, "Anthropic");
            fresh.SuggestedCategory = "SoftwareSubscriptions";
            (_, personal) = Seed(db, owner.Id, "Woolworths");
            personal.SuggestedCategory = "Personal";
            await db.SaveChangesAsync(Ct);
        }
        using var client = await Login(factory, "owner@kutax.test");
        var prefill = await (await client.PostAsync($"/api/transactions/{fresh.Id}/prefill", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("SoftwareSubscriptions", 80m), (prefill.GetProperty("category").GetString(), prefill.GetProperty("workUsePercent").GetDecimal()));
        var excluded = await (await client.PostAsync($"/api/transactions/{personal.Id}/prefill", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Excluded", excluded.GetProperty("category").GetString());
        using var stranger = await Login(factory, "stranger@kutax.test");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/transactions/{fresh.Id}/prefill", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_receipt_AI_is_reading_is_locked_until_it_finishes_or_the_person_takes_it_over()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        ReceiptTransaction queued, processing;
        ReceiptDocument queuedDocument, processingDocument;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            (queuedDocument, queued) = Seed(db, owner.Id, "Waiting");
            (processingDocument, processing) = Seed(db, owner.Id, "Reading");
            await db.SaveChangesAsync(Ct);
            await db.ProcessingJobs.Where(j => j.DocumentId == queuedDocument.Id).ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, "Queued"), Ct);
            await db.ProcessingJobs.Where(j => j.DocumentId == processingDocument.Id).ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, "Processing"), Ct);
        }
        using var client = await Login(factory, "owner@kutax.test");
        object Confirm(ReceiptTransaction t) => new { version = t.Version, merchant = "Synthetic Shop", date = "2025-09-01", amount = 20m, confirm = true };

        // While AI has it, typing, confirming, skipping and manual entry are all refused.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/transactions/{queued.Id}", Confirm(queued), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/transactions/{queued.Id}/dismiss", new { version = queued.Version, dismiss = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/documents/{queuedDocument.Id}/manual", null, Ct)).StatusCode);

        // A receipt being read right now can't be taken over; one still waiting in the queue can.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/documents/{processingDocument.Id}/take-over", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/documents/{queuedDocument.Id}/take-over", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/transactions/{queued.Id}", Confirm(queued), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/transactions/{processing.Id}", Confirm(processing), Ct)).StatusCode);
    }

    [Fact]
    public async Task Many_receipts_can_be_skipped_and_restored_at_once_and_ones_that_cannot_say_why()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        var stranger = await factory.CreateUserAsync("stranger@kutax.test", Password);
        ReceiptDocument first, second, split, reading, theirs;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            (first, _) = Seed(db, owner.Id, "first.pdf");
            (second, _) = Seed(db, owner.Id, "second.pdf");
            (split, var splitTransaction) = Seed(db, owner.Id, "split.pdf");
            db.Allocations.Add(new ExpenseAllocation { OwnerId = owner.Id, TransactionId = splitTransaction.Id, Description = "x", Amount = 20m, WorkUsePercent = 100, Basis = "x" });
            (reading, _) = Seed(db, owner.Id, "reading.pdf");
            (theirs, _) = Seed(db, stranger.Id, "theirs.pdf");
            await db.SaveChangesAsync(Ct);
            await db.ProcessingJobs.Where(j => j.DocumentId == reading.Id).ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, "Processing"), Ct);
        }
        using var client = await Login(factory, "owner@kutax.test");
        var response = await client.PostAsJsonAsync("/api/documents/skip-batch", new { documentIds = new[] { first.Id, second.Id, split.Id, reading.Id, theirs.Id }, skip = true, reason = "Duplicate" }, Ct);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, result.GetProperty("changed").GetInt32());
        var refused = result.GetProperty("notChanged").EnumerateArray().ToDictionary(x => x.GetProperty("documentId").GetGuid(), x => x.GetProperty("reason").GetString());
        Assert.Contains("split into expenses", refused[split.Id]);
        Assert.Equal("Still being read.", refused[reading.Id]);
        Assert.Equal("Not found.", refused[theirs.Id]); // another person's receipt is never touched
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            var skipped = await db.Transactions.Where(t => t.Status == "Dismissed").Select(t => new { t.DocumentId, t.SkipReason }).ToListAsync(Ct);
            Assert.Equal(new[] { first.Id, second.Id }.Order(), skipped.Select(t => t.DocumentId).Order());
            Assert.All(skipped, t => Assert.Equal("Duplicate", t.SkipReason));
        }
        var restored = await (await client.PostAsJsonAsync("/api/documents/skip-batch", new { documentIds = new[] { first.Id, second.Id }, skip = false }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, restored.GetProperty("changed").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/documents/skip-batch", new { documentIds = Array.Empty<Guid>(), skip = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/documents/skip-batch", new { documentIds = new[] { first.Id }, skip = true, reason = "Bored" }, Ct)).StatusCode);
    }
}
