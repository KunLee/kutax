namespace KuTax.Core.Claims;

/// <summary>One step of the resident income tax scale: income above <see cref="From"/> is taxed at <see cref="Rate"/>.</summary>
public sealed record TaxBracket(decimal From, decimal? To, decimal Rate);

/// <summary>A financial year's resident tax scale and Medicare levy, with where it was checked.</summary>
public sealed record TaxScale(int FinancialYearStart, TaxBracket[] Brackets, decimal MedicareLevy, string Source);

/// <summary>
/// Resident individual income tax rates, used only to estimate how much less tax a deduction means at a chosen
/// marginal rate. Not used to calculate any claim. Years not listed have no estimate.
/// Checked against the ATO's "Tax rates – Australian residents" page on 14 October 2026.
/// </summary>
public static class TaxRates
{
    private const string Source = "https://www.ato.gov.au/tax-rates-and-codes/tax-rates-australian-residents";

    private static TaxBracket[] Scale(decimal second) =>
    [
        new(0m, 18_200m, 0m),
        new(18_200m, 45_000m, second),
        new(45_000m, 135_000m, 0.30m),
        new(135_000m, 190_000m, 0.37m),
        new(190_000m, null, 0.45m),
    ];

    private static readonly TaxScale[] Scales =
    [
        new(2024, Scale(0.16m), 0.02m, Source),
        new(2025, Scale(0.16m), 0.02m, Source),
        // Legislated cut to the second rate from 1 July 2026 (Treasury Laws Amendment (Cost of Living Tax Cuts) Act 2025).
        new(2026, Scale(0.15m), 0.02m, Source),
    ];

    public static TaxScale? For(int financialYearStart) => Scales.SingleOrDefault(s => s.FinancialYearStart == financialYearStart);

    /// <summary>Tax saved by a deduction at a marginal rate (plus the Medicare levy above the tax-free threshold).</summary>
    public static decimal Saving(decimal deduction, TaxBracket bracket, decimal medicareLevy) =>
        bracket.Rate == 0m ? 0m : decimal.Round(deduction * (bracket.Rate + medicareLevy), 2, MidpointRounding.AwayFromZero);
}
