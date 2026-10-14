using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;
public sealed class ForeignCurrencyTests
{
    private const string Password = "Synthetic-Foreign-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Rba_table_is_read_by_its_units_row_and_amounts_convert_at_units_per_aud()
    {
        // Shape of the RBA F11.1 CSV: header rows, a Units row of codes, then daily rows (blank where unpublished).
        const string csv = "﻿F11.1  EXCHANGE RATES\r\nTitle,A$1=USD,Trade-weighted Index,A$1=JPY,A$1=SDR\r\nUnits,USD,Index,JPY,SDR\r\n"
            + "Series ID,FXRUSD,FXRTWI,FXRJY,FXRSDR\r\n08-Aug-2025,0.6512,60.10,96.31,0.48\r\n11-Aug-2025,0.6530,60.20,,0.48\r\n";
        var rates = RbaExchangeRates.Parse(csv);
        Assert.Equal(3, rates.Count); // the index and SDR columns are not currencies; a blank cell is skipped
        Assert.Contains(rates, r => r is { Currency: "USD", UnitsPerAud: 0.6512m } && r.Date == new DateOnly(2025, 8, 8));
        Assert.DoesNotContain(rates, r => r.Currency == "JPY" && r.Date == new DateOnly(2025, 8, 11));
        Assert.Equal(7.43m, ForeignCurrency.ToAud(4.84m, 0.6512m));
        Assert.Equal(1m, ForeignCurrency.ToAud(0.65m, 0.65m));
        Assert.False(ForeignCurrency.IsCode("AUD"));
        Assert.False(ForeignCurrency.IsCode("usd"));
        Assert.True(ForeignCurrency.IsCode("USD"));
    }

    [Fact]
    public void Ai_original_currency_is_kept_only_when_valid_and_traceable()
    {
        var today = new DateOnly(2026, 10, 11);
        var usd = new ReceiptCandidate(new("Anysphere", .95m, "Anysphere, Inc."), new("2025-08-09", .95m, "August 9, 2025"), new(null, 0m, "USD 4.84"),
            new(null, 0m, null), "AUD", new("USD", .97m, "$4.84 USD"), new(4.84m, .97m, "Total $4.84"));
        var kept = ReceiptCandidate.Sanitize(JsonSerializer.Serialize(usd, ReceiptCandidate.Json), today);
        Assert.Equal(("USD", 4.84m), (kept.OriginalCurrency!.Value, kept.OriginalAmount!.Value));
        var bad = ReceiptCandidate.Sanitize(JsonSerializer.Serialize(usd with { OriginalCurrency = new("AUD", .9m, "AUD") }, ReceiptCandidate.Json), today);
        Assert.Null(bad.OriginalCurrency);
        Assert.Null(bad.OriginalAmount); // an amount means nothing without a valid currency
        var untraceable = ReceiptCandidate.Sanitize(JsonSerializer.Serialize(usd with { OriginalAmount = new(4.84m, .9m, null) }, ReceiptCandidate.Json), today);
        Assert.Null(untraceable.OriginalAmount);
    }

    [Fact]
    public async Task Foreign_receipts_save_a_statement_amount_or_an_rba_estimate_that_matches_the_published_rate()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        Guid transactionId; Guid version;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            var batch = new ImportBatch { OwnerId = owner.Id, ExpectedFiles = 1 };
            var document = new ReceiptDocument { OwnerId = owner.Id, ImportBatchId = batch.Id, OriginalFileName = "invoice.pdf", RelativePath = "invoice.pdf", ObjectKey = "k", Sha256 = "s", ContentType = "application/pdf", ScanStatus = "Clean", Status = "NeedsReview" };
            var transaction = new ReceiptTransaction { DocumentId = document.Id, OwnerId = owner.Id };
            db.AddRange(batch, document, transaction);
            // Friday's rate; the receipt below is dated the following Sunday.
            db.ExchangeRates.Add(new ExchangeRate { Currency = "USD", Date = new DateOnly(2025, 8, 8), UnitsPerAud = 0.6512m });
            await db.SaveChangesAsync(Ct);
            (transactionId, version) = (transaction.Id, transaction.Version);
        }
        using var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email = owner.Email, password = Password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);

        var quote = await client.GetFromJsonAsync<JsonElement>("/api/exchange-rates/USD/2025-08-10?amount=4.84", Ct);
        Assert.Equal(("2025-08-08", 7.43m), (quote.GetProperty("rateDate").GetString(), quote.GetProperty("aud").GetDecimal()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/exchange-rates/USD/2024-01-15", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/exchange-rates/AUD/2025-08-10", Ct)).StatusCode);

        object Save(decimal amount, string source, string date = "2025-08-10") => new { version, merchant = "Anysphere, Inc.", date, amount, gst = (decimal?)null, notes = "", confirm = false,
            foreign = new { currency = "USD", amount = 4.84m, source } };
        var wrong = await client.PutAsJsonAsync($"/api/transactions/{transactionId}", Save(8.00m, "RBA"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Contains("A$7.43", await wrong.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/transactions/{transactionId}", Save(7.43m, "RBA", "2024-01-15"), Ct)).StatusCode);

        var estimated = await client.PutAsJsonAsync($"/api/transactions/{transactionId}", Save(7.43m, "RBA"), Ct);
        Assert.Equal(HttpStatusCode.OK, estimated.StatusCode);
        var saved = await estimated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("USD", 4.84m, "RBA", 0.6512m, "2025-08-08"), (saved.GetProperty("originalCurrency").GetString(), saved.GetProperty("originalAmount").GetDecimal(),
            saved.GetProperty("conversionSource").GetString(), saved.GetProperty("conversionRate").GetDecimal(), saved.GetProperty("conversionRateDate").GetString()));

        // A statement amount is taken as entered (it includes the bank's rate and fees) and carries no RBA rate.
        version = saved.GetProperty("version").GetGuid();
        var statement = await client.PutAsJsonAsync($"/api/transactions/{transactionId}", Save(7.61m, "Statement"), Ct);
        Assert.Equal(HttpStatusCode.OK, statement.StatusCode);
        var fromStatement = await statement.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((7.61m, "Statement", JsonValueKind.Null), (fromStatement.GetProperty("amount").GetDecimal(), fromStatement.GetProperty("conversionSource").GetString(), fromStatement.GetProperty("conversionRate").ValueKind));

        version = fromStatement.GetProperty("version").GetGuid();
        var invalid = new { version, merchant = "Anysphere, Inc.", date = "2025-08-10", amount = 7.61m, notes = "", confirm = false, foreign = new { currency = "AUD", amount = 4.84m, source = "Statement" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/transactions/{transactionId}", invalid, Ct)).StatusCode);
    }
}
