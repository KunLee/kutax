using KuTax.Core.FinancialYears;

namespace KuTax.Api.Tests;

public sealed class FinancialYearTests
{
    [Theory]
    [InlineData("2026-06-30", 2025)]
    [InlineData("2026-07-01", 2026)]
    [InlineData("2024-02-29", 2023)]
    public void Date_boundaries_use_July_to_June(string input, int expectedStart)
    {
        var year = FinancialYear.FromDate(DateOnly.Parse(input));
        Assert.Equal(expectedStart, year.StartYear);
        Assert.Equal(new DateOnly(expectedStart, 7, 1), year.Start);
        Assert.Equal(new DateOnly(expectedStart + 1, 6, 30), year.End);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(9999)]
    public void Out_of_range_year_is_rejected(int input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FinancialYear(input));

    [Theory]
    [InlineData("2026-10-11", 2025)]
    [InlineData("2026-07-01", 2025)]
    [InlineData("2026-11-01", 2026)]
    [InlineData("2027-03-15", 2026)]
    public void New_accounts_open_on_the_year_being_lodged_at_tax_time(string today, int expected) =>
        Assert.Equal(expected, FinancialYear.DefaultForNewAccount(DateOnly.Parse(today)).StartYear);
}
