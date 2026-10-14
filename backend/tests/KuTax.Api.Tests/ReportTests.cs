using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;

/// <summary>Reports cover only the records being worked on, for the chosen financial year.</summary>
public sealed class ReportTests
{
    private const string Password = "Synthetic-Reports-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_year_report_adds_up_spending_by_month_and_merchant_for_your_records_only()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        var other = await factory.CreateUserAsync("other@kutax.test", Password);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            void Receipt(string ownerId, string merchant, DateOnly date, decimal amount, string status)
            {
                var batch = new ImportBatch { OwnerId = ownerId, ExpectedFiles = 1 };
                var document = new ReceiptDocument { OwnerId = ownerId, ImportBatchId = batch.Id, OriginalFileName = "r.pdf", RelativePath = "r.pdf", ObjectKey = Guid.NewGuid().ToString(), Sha256 = Guid.NewGuid().ToString("N"), ContentType = "application/pdf", ScanStatus = "Clean", Status = "Reviewed" };
                db.AddRange(batch, document, new ReceiptTransaction { DocumentId = document.Id, OwnerId = ownerId, Merchant = merchant, Date = date, Amount = amount, Status = status });
            }
            Receipt(owner.Id, "Synthetic Books", new(2025, 7, 3), 40m, "Confirmed");
            Receipt(owner.Id, "synthetic books", new(2025, 7, 20), 10m, "NeedsReview");
            Receipt(owner.Id, "Synthetic Cafe", new(2026, 6, 30), 5m, "Dismissed");
            Receipt(owner.Id, "Earlier Year Shop", new(2025, 6, 30), 99m, "Confirmed");
            Receipt(other.Id, "Someone Else", new(2025, 8, 1), 500m, "Confirmed");
            // A monthly phone bill with September missing.
            foreach (var month in new[] { 7, 8, 10, 11 }) Receipt(owner.Id, "Synthetic Telco", new(2025, month, 15), 60m, "Confirmed");
            await db.SaveChangesAsync(Ct);
        }
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@kutax.test", password = Password }, Ct)).StatusCode);

        var report = await client.GetFromJsonAsync<JsonElement>("/api/reports/2025", Ct);
        var totals = report.GetProperty("totals");
        Assert.Equal((7, 290m, 1, 1), (totals.GetProperty("receipts").GetInt32(), totals.GetProperty("spent").GetDecimal(), totals.GetProperty("skipped").GetInt32(), totals.GetProperty("toCheck").GetInt32()));
        var months = report.GetProperty("months").EnumerateArray().ToArray();
        Assert.Equal(12, months.Length);
        Assert.Equal(("2025-07", 110m), (months[0].GetProperty("month").GetString(), months[0].GetProperty("spent").GetDecimal()));
        Assert.Equal(0m, months[11].GetProperty("spent").GetDecimal()); // the skipped receipt isn't spending
        var merchant = report.GetProperty("merchants").EnumerateArray().Single(m => string.Equals(m.GetProperty("merchant").GetString(), "Synthetic Books", StringComparison.OrdinalIgnoreCase));
        Assert.Equal((50m, 2), (merchant.GetProperty("spent").GetDecimal(), merchant.GetProperty("receipts").GetInt32()));
        // The monthly bill's missing month is pointed out; the two-receipt bookshop isn't a monthly bill.
        var missing = Assert.Single(report.GetProperty("missing").EnumerateArray());
        Assert.Equal(("Synthetic Telco", 60m), (missing.GetProperty("merchant").GetString(), missing.GetProperty("typical").GetDecimal()));
        Assert.Equal(["2025-09"], missing.GetProperty("missing").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(0.16m, report.GetProperty("rates").GetProperty("brackets")[1].GetProperty("rate").GetDecimal());
        Assert.Equal([2024, 2025], report.GetProperty("years").EnumerateArray().Select(y => y.GetProperty("year").GetInt32()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/1", Ct)).StatusCode);
    }
}

/// <summary>The tax-rate table only estimates savings; years without a checked scale have none.</summary>
public sealed class TaxRateTests
{
    [Fact]
    public void A_deduction_saves_its_marginal_rate_plus_the_medicare_levy()
    {
        var scale = KuTax.Core.Claims.TaxRates.For(2025)!;
        Assert.Equal(320m, KuTax.Core.Claims.TaxRates.Saving(1000m, scale.Brackets[2], scale.MedicareLevy)); // 30% + 2%
        Assert.Equal(0m, KuTax.Core.Claims.TaxRates.Saving(1000m, scale.Brackets[0], scale.MedicareLevy)); // under the tax-free threshold
        Assert.Equal(0.15m, KuTax.Core.Claims.TaxRates.For(2026)!.Brackets[1].Rate);
        Assert.Null(KuTax.Core.Claims.TaxRates.For(2019));
    }
}
