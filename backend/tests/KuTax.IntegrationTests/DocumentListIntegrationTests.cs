using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.IntegrationTests;
public sealed class DocumentListIntegrationTests
{
    [RequiresServices]
    public async Task Review_state_filter_and_counts_are_owner_scoped_in_postgres()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>(); await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser NewUser() { var email=$"list-{Guid.NewGuid():N}@kutax.test"; return new(){UserName=email,Email=email}; }
        var owner = NewUser(); var other = NewUser(); const string password = "Synthetic-List-Test-481!";
        Assert.True((await users.CreateAsync(owner,password)).Succeeded); Assert.True((await users.CreateAsync(other,password)).Succeeded);
        try
        {
            // Rows are seeded directly: the list reads metadata only and never opens stored objects.
            var batches = new Dictionary<string, ImportBatch> { [owner.Id] = new() { OwnerId = owner.Id, ExpectedFiles = 5 }, [other.Id] = new() { OwnerId = other.Id, ExpectedFiles = 1 } };
            db.ImportBatches.AddRange(batches.Values);
            ReceiptDocument Document(string ownerId, string name, string status) => new() { OwnerId = ownerId, ImportBatchId = batches[ownerId].Id, OriginalFileName = name,
                RelativePath = name, ObjectKey = Guid.NewGuid().ToString("N"), Sha256 = Guid.NewGuid().ToString("N"), ContentType = "application/pdf", Status = status };
            var draft = Document(owner.Id, "draft.pdf", "NeedsReview"); var confirmed = Document(owner.Id, "confirmed.pdf", "NeedsReview"); var skipped = Document(owner.Id, "skipped.pdf", "NeedsReview");
            db.Documents.AddRange(draft, confirmed, skipped, Document(owner.Id, "queued.pdf", "Stored"), Document(owner.Id, "failed.pdf", "ProcessingFailed"),
                Document(owner.Id, "blocked.pdf", "Quarantined"), Document(other.Id, "other.pdf", "NeedsReview"));
            db.Transactions.AddRange(
                new ReceiptTransaction { DocumentId = draft.Id, OwnerId = owner.Id, Merchant = "Synthetic Union", Date = new DateOnly(2025, 8, 1), Amount = 42.50m },
                new ReceiptTransaction { DocumentId = confirmed.Id, OwnerId = owner.Id, Merchant = "Synthetic Institute", Amount = 10m, Status = "Confirmed" },
                new ReceiptTransaction { DocumentId = skipped.Id, OwnerId = owner.Id, Merchant = "Synthetic Grocer", Amount = 5m, Status = "Dismissed", SkipReason = "Personal" });
            db.ExtractionAttempts.Add(new ExtractionAttempt { DocumentId = draft.Id, OwnerId = owner.Id, Provider = "Groq", Model = "synthetic", CandidateJson = "{}" });
            await db.SaveChangesAsync();
            using var client = ReceiptIntegrationTests.CreateClient(factory);
            await ReceiptIntegrationTests.Login(client, owner.Email!, password);

            var all = await client.GetFromJsonAsync<JsonElement>("/api/documents/");
            Assert.Equal(6, all.GetProperty("total").GetInt32());
            var counts = all.GetProperty("counts");
            Assert.Equal(6, counts.GetProperty("total").GetInt32());
            Assert.Equal(1, counts.GetProperty("notClaimed").GetInt32());
            Assert.Equal(2, counts.GetProperty("needsReview").GetInt32());
            Assert.Equal(1, counts.GetProperty("processing").GetInt32());
            Assert.Equal(1, counts.GetProperty("confirmed").GetInt32());
            Assert.Equal(1, counts.GetProperty("blocked").GetInt32());
            Assert.DoesNotContain(all.GetProperty("items").EnumerateArray(), item => item.GetProperty("originalFileName").GetString() == "other.pdf");

            var review = await client.GetFromJsonAsync<JsonElement>("/api/documents/?state=review");
            var items = review.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("originalFileName").GetString()!);
            Assert.Equal(["draft.pdf", "failed.pdf"], items.Keys.Order());
            // AI-read receipts lead the queue and say so.
            Assert.Equal("draft.pdf", review.GetProperty("items")[0].GetProperty("originalFileName").GetString());
            Assert.True(items["draft.pdf"].GetProperty("aiRead").GetBoolean());
            Assert.False(items["failed.pdf"].GetProperty("aiRead").GetBoolean());
            Assert.Equal(2, review.GetProperty("total").GetInt32());
            Assert.Equal("NeedsReview", items["draft.pdf"].GetProperty("reviewState").GetString());
            Assert.Equal("Synthetic Union", items["draft.pdf"].GetProperty("merchant").GetString());
            Assert.Equal(42.50m, items["draft.pdf"].GetProperty("amount").GetDecimal());
            Assert.Equal("2025-08-01", items["draft.pdf"].GetProperty("date").GetString());
            Assert.Equal(JsonValueKind.Null, items["failed.pdf"].GetProperty("merchant").ValueKind);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/documents/?state=everything")).StatusCode);
            // Skipped receipts have their own list, with the reason.
            var skippedList = await client.GetFromJsonAsync<JsonElement>("/api/documents/?state=skipped");
            var skippedItem = Assert.Single(skippedList.GetProperty("items").EnumerateArray());
            Assert.Equal(("skipped.pdf", "Personal", "NotClaimed"), (skippedItem.GetProperty("originalFileName").GetString(), skippedItem.GetProperty("skipReason").GetString(), skippedItem.GetProperty("reviewState").GetString()));
            // Search by merchant, amount or date; sort by date, merchant or amount, with receipts lacking the value last.
            async Task<string[]> Files(string query) => (await client.GetFromJsonAsync<JsonElement>("/api/documents/?" + query)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("originalFileName").GetString()!).ToArray();
            Assert.Equal(["draft.pdf"], await Files("search=42.5"));
            Assert.Equal(["confirmed.pdf"], await Files("search=institute"));
            Assert.Equal(["draft.pdf"], await Files("search=2025-08-01"));
            Assert.Equal(["skipped.pdf", "confirmed.pdf", "draft.pdf"], (await Files("sort=merchant")).Take(3));
            Assert.Equal(["draft.pdf", "confirmed.pdf", "skipped.pdf"], (await Files("sort=amount")).Take(3));
            Assert.Equal("draft.pdf", (await Files("sort=date"))[0]);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/documents/?sort=random")).StatusCode);

            // One copy typed in AUD and another read in USD: the amount finds both, and each is flagged as a possible duplicate.
            var inAud = Document(owner.Id, "huize-aud.pdf", "NeedsReview"); var inUsd = Document(owner.Id, "huize-usd.pdf", "NeedsReview");
            db.Documents.AddRange(inAud, inUsd);
            db.Transactions.AddRange(
                new ReceiptTransaction { DocumentId = inAud.Id, OwnerId = owner.Id, Merchant = "Synthetic Huize Ltd", Date = new DateOnly(2026, 7, 3), Amount = 71.90m },
                new ReceiptTransaction { DocumentId = inUsd.Id, OwnerId = owner.Id, Merchant = "SYNTHETIC HUIZE LTD", Date = new DateOnly(2026, 7, 3), Amount = 103.59m, OriginalCurrency = "USD", OriginalAmount = 71.90m });
            await db.SaveChangesAsync();
            Assert.Equal(["huize-aud.pdf", "huize-usd.pdf"], (await Files("search=71.9")).Order());
            Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/documents/{inAud.Id}/review")).GetProperty("possibleDuplicate").GetBoolean());
            Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/documents/{inUsd.Id}/review")).GetProperty("possibleDuplicate").GetBoolean());
            // A financial year shows only receipts dated in it (1 July to 30 June); undated ones stay out.
            Assert.Equal(["draft.pdf"], await Files("year=2025"));
            Assert.Equal(["huize-aud.pdf", "huize-usd.pdf"], (await Files("year=2026")).Order());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/documents/?year=1999")).StatusCode);
            // From a report: one month, one merchant (any case) or one kind of expense.
            var union = await db.Transactions.SingleAsync(t => t.DocumentId == draft.Id);
            db.Allocations.Add(new KuTax.Core.Claims.ExpenseAllocation { TransactionId = union.Id, OwnerId = owner.Id, Category = "UnionFees", Amount = 42.50m, WorkUsePercent = 100m });
            await db.SaveChangesAsync();
            Assert.Equal(["huize-aud.pdf", "huize-usd.pdf"], (await Files("month=2026-07")).Order());
            Assert.Equal(["huize-aud.pdf", "huize-usd.pdf"], (await Files("merchant=synthetic%20huize%20ltd")).Order());
            Assert.Equal(["draft.pdf"], await Files("category=UnionFees"));
            Assert.Empty(await Files("category=SelfEducation"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/documents/?month=July")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/documents/?category=Union%27%3B")).StatusCode);
        }
        finally
        {
            db.ChangeTracker.Clear();
            foreach (var id in new[] { owner.Id, other.Id })
            {
                await db.ExtractionAttempts.Where(a => a.OwnerId == id).ExecuteDeleteAsync();
                await db.Allocations.Where(a => a.OwnerId == id).ExecuteDeleteAsync();
                await db.Transactions.Where(t => t.OwnerId == id).ExecuteDeleteAsync();
                await db.Documents.Where(d => d.OwnerId == id).ExecuteDeleteAsync();
                await db.ImportBatches.Where(b => b.OwnerId == id).ExecuteDeleteAsync();
            }
            await users.DeleteAsync(owner); await users.DeleteAsync(other);
        }
    }

    [RequiresServices]
    public async Task Not_a_work_expense_leaves_the_queue_and_annual_exceptions_and_can_be_restored()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>(); await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email=$"dismiss-{Guid.NewGuid():N}@kutax.test"; var owner=new ApplicationUser{UserName=email,Email=email}; const string password="Synthetic-Dismiss-481!";
        Assert.True((await users.CreateAsync(owner,password)).Succeeded);
        try
        {
            var batch=new ImportBatch{OwnerId=owner.Id,ExpectedFiles=1};db.ImportBatches.Add(batch);
            var document=new ReceiptDocument{OwnerId=owner.Id,ImportBatchId=batch.Id,OriginalFileName="bill.pdf",RelativePath="bill.pdf",ObjectKey=Guid.NewGuid().ToString("N"),Sha256=Guid.NewGuid().ToString("N"),ContentType="application/pdf",Status="NeedsReview",ScanStatus="Clean"};
            var transaction=new ReceiptTransaction{DocumentId=document.Id,OwnerId=owner.Id};
            db.Documents.Add(document);db.Transactions.Add(transaction);await db.SaveChangesAsync();
            using var client = ReceiptIntegrationTests.CreateClient(factory); await ReceiptIntegrationTests.Login(client, email, password);
            async Task<JsonElement> Counts()=>(await client.GetFromJsonAsync<JsonElement>("/api/documents/?state=review")).GetProperty("counts");
            async Task<int> Undated()=>(await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/claims")).GetProperty("issues").EnumerateArray().Count(i=>i.GetProperty("documentId").GetGuid()==document.Id);
            Assert.Equal(1,(await Counts()).GetProperty("needsReview").GetInt32());
            Assert.Equal(1,await Undated());

            Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/transactions/{transaction.Id}/dismiss",new{version=Guid.NewGuid(),dismiss=true})).StatusCode);
            var dismissed=await client.PutAsJsonAsync($"/api/transactions/{transaction.Id}/dismiss",new{version=transaction.Version,dismiss=true});
            Assert.Equal(HttpStatusCode.OK,dismissed.StatusCode);
            var counts=await Counts();
            Assert.Equal((0,1),(counts.GetProperty("needsReview").GetInt32(),counts.GetProperty("notClaimed").GetInt32()));
            Assert.Equal(0,await Undated());
            var all=await client.GetFromJsonAsync<JsonElement>("/api/documents/");
            Assert.Equal("NotClaimed",all.GetProperty("items")[0].GetProperty("reviewState").GetString());

            var version=(await dismissed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetGuid();
            Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/transactions/{transaction.Id}/dismiss",new{version,dismiss=false})).StatusCode);
            Assert.Equal(1,(await Counts()).GetProperty("needsReview").GetInt32());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AuditEvents.Where(a=>a.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.Transactions.Where(t=>t.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.Documents.Where(d=>d.OwnerId==owner.Id).ExecuteDeleteAsync();
            await db.ImportBatches.Where(b=>b.OwnerId==owner.Id).ExecuteDeleteAsync();
            await users.DeleteAsync(owner);
        }
    }
}
