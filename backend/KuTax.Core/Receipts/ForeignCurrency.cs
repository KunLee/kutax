namespace KuTax.Core.Receipts;

/// <summary>
/// Converting foreign-currency receipts to AUD. The ATO translates an expense at the exchange rate when it was
/// incurred or paid (RBA daily rates since 2020); the AUD amount actually charged on a statement is the best evidence.
/// </summary>
public static class ForeignCurrency
{
    public const decimal MaxAmount = 1_000_000_000_000m;
    /// <summary>Currencies in the RBA F11.1 daily exchange-rate table.</summary>
    public static readonly string[] RbaCurrencies = ["USD", "EUR", "GBP", "NZD", "JPY", "CNY", "SGD", "HKD", "CAD", "CHF", "INR", "KRW", "THB", "TWD", "MYR", "IDR", "VND", "PHP", "AED", "PGK", "ZAR"];
    public static readonly string[] Sources = ["Statement", "RBA"];

    public static bool IsCode(string? code) => code is { Length: 3 } && code.All(char.IsAsciiLetterUpper) && code != "AUD";

    /// <summary>RBA quotes are "A$1 = X units", so AUD = foreign amount / X, rounded to cents.</summary>
    public static decimal ToAud(decimal originalAmount, decimal unitsPerAud) =>
        unitsPerAud <= 0 ? throw new ArgumentOutOfRangeException(nameof(unitsPerAud)) : decimal.Round(originalAmount / unitsPerAud, 2, MidpointRounding.AwayFromZero);
}

/// <summary>One RBA daily rate: units of <see cref="Currency"/> per A$1 on <see cref="Date"/>.</summary>
public sealed class ExchangeRate
{
    public string Currency { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal UnitsPerAud { get; set; }
}
