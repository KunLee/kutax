using System.Globalization;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KuTax.Infrastructure.Processing;

/// <summary>An RBA rate used for a conversion: <see cref="UnitsPerAud"/> of the currency per A$1 on <see cref="RateDate"/>.</summary>
public sealed record ExchangeRateQuote(string Currency, DateOnly RequestedDate, DateOnly RateDate, decimal UnitsPerAud);

public interface IExchangeRateSource
{
    Task<ExchangeRateQuote?> FindAsync(string currency, DateOnly date, CancellationToken ct);
}

/// <summary>
/// RBA daily exchange rates (statistical table F11.1, 3 January 2023 onwards), stored locally and refreshed at most hourly
/// when a requested date is newer than what is stored. Only public rate data is fetched; nothing about the person is sent.
/// </summary>
public sealed class RbaExchangeRates(KuTaxDbContext db, IHttpClientFactory clients, ILogger<RbaExchangeRates> logger) : IExchangeRateSource
{
    public const string SourceUrl = "https://www.rba.gov.au/statistics/tables/csv/f11.1-data.csv";
    // RBA publishes business days only: a weekend or holiday uses the latest earlier rate, up to a week back.
    private const int LookbackDays = 7;
    private const int MaxBytes = 5 * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset lastRefresh = DateTimeOffset.MinValue;

    public async Task<ExchangeRateQuote?> FindAsync(string currency, DateOnly date, CancellationToken ct)
    {
        if (!ForeignCurrency.RbaCurrencies.Contains(currency)) return null;
        var quote = await LookupAsync(currency, date, ct);
        // An exact match, or nothing newer could exist yet: no download needed.
        if (quote is not null && quote.RateDate == date) return quote;
        var newest = await db.ExchangeRates.Where(r => r.Currency == currency).MaxAsync(r => (DateOnly?)r.Date, ct);
        if (newest is null || newest < date) await RefreshAsync(ct);
        return await LookupAsync(currency, date, ct);
    }

    private async Task<ExchangeRateQuote?> LookupAsync(string currency, DateOnly date, CancellationToken ct)
    {
        var from = date.AddDays(-LookbackDays);
        var rate = await db.ExchangeRates.AsNoTracking().Where(r => r.Currency == currency && r.Date <= date && r.Date >= from)
            .OrderByDescending(r => r.Date).FirstOrDefaultAsync(ct);
        return rate is null ? null : new ExchangeRateQuote(currency, date, rate.Date, rate.UnitsPerAud);
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - lastRefresh < TimeSpan.FromHours(1)) return;
            lastRefresh = DateTimeOffset.UtcNow;
            using var response = await clients.CreateClient("rba").GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("RBA table too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var bounded = new MemoryStream();
            var buffer = new byte[16384]; int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (bounded.Length + count > MaxBytes) throw new InvalidDataException("RBA table too large.");
                await bounded.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            var rates = Parse(System.Text.Encoding.UTF8.GetString(bounded.ToArray()));
            if (rates.Count == 0) throw new InvalidDataException("RBA table had no rates.");
            var first = rates.Min(r => r.Date);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.ExchangeRates.Where(r => r.Date >= first).ExecuteDeleteAsync(ct);
            db.ExchangeRates.AddRange(rates);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            logger.LogInformation("Stored {Count} RBA exchange rates from {First} to {Last}.", rates.Count, first, rates.Max(r => r.Date));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidDataException or DbUpdateException)
        {
            // Rates stay as they were; the person can still use the AUD amount from their statement.
            logger.LogWarning("Could not refresh RBA exchange rates ({ExceptionType}).", error.GetType().Name);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Reads the F11.1 CSV: currency codes come from its "Units" row, data rows start with a date like 09-Oct-2026.</summary>
    internal static List<ExchangeRate> Parse(string csv)
    {
        var rates = new List<ExchangeRate>();
        string[]? units = null;
        foreach (var raw in csv.Split('\n'))
        {
            var cells = raw.TrimEnd('\r').TrimStart('﻿').Split(',');
            if (cells[0] == "Units") { units = cells; continue; }
            if (units is null || !DateOnly.TryParseExact(cells[0], "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            for (var i = 1; i < cells.Length && i < units.Length; i++)
                if (ForeignCurrency.RbaCurrencies.Contains(units[i])
                    && decimal.TryParse(cells[i], NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0)
                    rates.Add(new ExchangeRate { Currency = units[i], Date = date, UnitsPerAud = value });
        }
        return rates;
    }
}
