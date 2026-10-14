namespace KuTax.Core.FinancialYears;

// Dates describe record organisation, not eligibility or tax calculations.
public readonly record struct FinancialYear
{
    public int StartYear { get; }
    public DateOnly Start => new(StartYear, 7, 1);
    public DateOnly End => new(StartYear + 1, 6, 30);
    public string Label => $"FY {StartYear}–{(StartYear + 1) % 100:00}";

    public FinancialYear(int startYear)
    {
        if (startYear is < 1900 or > 9998)
            throw new ArgumentOutOfRangeException(nameof(startYear));
        StartYear = startYear;
    }

    public static FinancialYear FromDate(DateOnly date) =>
        new(date.Month >= 7 ? date.Year : date.Year - 1);

    /// <summary>
    /// The year a new account should open on. From July to October people are preparing the
    /// year that just ended, so that is more useful than the year that has only just begun.
    /// </summary>
    public static FinancialYear DefaultForNewAccount(DateOnly today) =>
        today.Month is >= 7 and <= 10 ? new(today.Year - 1) : FromDate(today);
}
