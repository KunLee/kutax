using System.Security.Claims;
using KuTax.Api.Features.Claims;
using KuTax.Core.Claims;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static KuTax.Api.Features.Sharing.Taxpayer;

namespace KuTax.Api.Features.Reports;

/// <summary>
/// Reports on one financial year of the records being worked on (yours, or those shared with you): spending by month,
/// deductions by kind of expense, the merchants you spend most with, how claims stand, and how the year compares with
/// earlier ones. Figures come from the same assessment as the tax summary, so they always agree with it.
/// </summary>
public static class ReportEndpoints
{
    private const int EarlierYears = 4;

    public static void MapReports(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/reports/{year:int}", async (int year, ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            if (year is < 1900 or > 9998) return Results.BadRequest();
            var owner = Owner(principal);
            var from = new DateOnly(year, 7, 1);
            var to = from.AddYears(1);
            var receipts = await db.Transactions.AsNoTracking().Where(t => t.OwnerId == owner && t.Date >= from && t.Date < to)
                .Select(t => new { t.Merchant, Date = t.Date!.Value, t.Amount, t.Status, t.OriginalCurrency, t.OriginalAmount }).ToListAsync(ct);
            var kept = receipts.Where(r => r.Status != "Dismissed").ToList();
            var assessment = await AnnualAssessment.Load(db, owner, year, ct);
            var rows = assessment.Rows;

            var months = Enumerable.Range(0, 12).Select(i => from.AddMonths(i)).Select(month => new
            {
                Month = month.ToString("yyyy-MM"),
                Spent = kept.Where(r => r.Date.Year == month.Year && r.Date.Month == month.Month).Sum(r => r.Amount ?? 0),
                Claimed = rows.Where(r => r.Status == "Approved" && r.Date.Year == month.Year && r.Date.Month == month.Month).Sum(r => r.DeductibleAmount ?? 0),
                Receipts = receipts.Count(r => r.Date.Year == month.Year && r.Date.Month == month.Month),
            });
            var categories = rows.GroupBy(r => r.Category).Select(g => new
            {
                Category = g.Key,
                Claimed = g.Where(r => r.Status == "Approved").Sum(r => r.DeductibleAmount ?? 0),
                Ready = g.Where(r => r.Status == "Candidate").Sum(r => r.DeductibleAmount ?? 0),
                Spent = g.Sum(r => r.ExpenseAmount),
                Expenses = g.Count(),
            }).OrderByDescending(c => c.Claimed + c.Ready).ThenByDescending(c => c.Spent);
            var merchants = kept.GroupBy(r => string.IsNullOrWhiteSpace(r.Merchant) ? "" : r.Merchant.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Merchant = g.First().Merchant?.Trim() ?? "", Spent = g.Sum(r => r.Amount ?? 0), Receipts = g.Count() })
                .OrderByDescending(m => m.Spent).Take(8);
            var currencies = kept.GroupBy(r => r.OriginalCurrency ?? "AUD")
                .Select(g => new { Currency = g.Key, Receipts = g.Count(), Spent = g.Sum(r => r.Amount ?? 0), Original = g.Sum(r => r.OriginalAmount ?? r.Amount ?? 0) })
                .OrderByDescending(c => c.Receipts);

            // Earlier years, from the same assessment (only years with receipts are worked out).
            var earlierStarts = await db.Transactions.AsNoTracking().Where(t => t.OwnerId == owner && t.Date < from && t.Date >= from.AddYears(-EarlierYears))
                .Select(t => t.Date!.Value).ToListAsync(ct);
            var history = new List<object>();
            YearAssessment? previous = null;
            foreach (var start in earlierStarts.Select(d => d.Month >= 7 ? d.Year : d.Year - 1).Distinct().Order())
            {
                var earlier = await AnnualAssessment.Load(db, owner, start, ct);
                if (start == year - 1) previous = earlier;
                history.Add(new { Year = start, Claimed = earlier.ApprovedTotal, Spent = earlier.Rows.Sum(r => r.ExpenseAmount) });
            }

            // Bills that come every month (phone, internet, subscriptions): months with no receipt between the first and
            // the last one this year are probably receipts still to add. Quarterly or occasional spending isn't flagged.
            var categoryOf = rows.GroupBy(r => r.Merchant.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.GroupBy(r => r.Category).OrderByDescending(c => c.Count()).First().Key, StringComparer.OrdinalIgnoreCase);
            var missing = kept.Where(r => !string.IsNullOrWhiteSpace(r.Merchant))
                .GroupBy(r => r.Merchant!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var seen = g.Select(r => new DateOnly(r.Date.Year, r.Date.Month, 1)).Distinct().Order().ToList();
                    var span = seen.Count == 0 ? 0 : (seen[^1].Year - seen[0].Year) * 12 + seen[^1].Month - seen[0].Month + 1;
                    var gaps = Enumerable.Range(0, span).Select(i => seen[0].AddMonths(i)).Where(m => !seen.Contains(m)).ToList();
                    var amounts = g.Select(r => r.Amount ?? 0).Order().ToList();
                    return new { Merchant = g.First().Merchant!.Trim(), Months = seen.Count, Span = span, Gaps = gaps, Typical = amounts[amounts.Count / 2],
                        Category = categoryOf.GetValueOrDefault(g.Key) };
                })
                .Where(m => m.Months >= 3 && m.Gaps.Count > 0 && m.Months * 2 >= m.Span)
                .OrderByDescending(m => m.Gaps.Count * m.Typical).Take(6)
                .Select(m => new { m.Merchant, m.Months, m.Typical, m.Category, Missing = m.Gaps.Select(d => d.ToString("yyyy-MM")) });

            // Kinds of expense claimed last year with nothing (or much less) so far this year.
            var compare = previous is null ? [] : previous.Rows.Where(r => r.Status == "Approved" && r.Category is not ("Unsupported" or "Excluded"))
                .GroupBy(r => r.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    LastYear = g.Sum(r => r.DeductibleAmount ?? 0),
                    ThisYear = rows.Where(r => r.Category == g.Key && r.Status is "Approved" or "Candidate").Sum(r => r.DeductibleAmount ?? 0),
                })
                .Where(c => c.LastYear > 0 && c.ThisYear < c.LastYear / 2)
                .OrderByDescending(c => c.LastYear - c.ThisYear).ToList();
            var scale = TaxRates.For(year);
            history.Add(new { Year = year, Claimed = assessment.ApprovedTotal, Spent = rows.Sum(r => r.ExpenseAmount) });

            return Results.Ok(new
            {
                year,
                totals = new
                {
                    Receipts = receipts.Count,
                    Spent = kept.Sum(r => r.Amount ?? 0),
                    Claimed = assessment.ApprovedTotal,
                    Ready = assessment.CandidateTotal,
                    Skipped = receipts.Count(r => r.Status == "Dismissed"),
                    ToCheck = receipts.Count(r => r.Status is not ("Confirmed" or "Dismissed")),
                    Expenses = rows.Length,
                },
                claims = new
                {
                    Approved = rows.Count(r => r.Status == "Approved"),
                    Ready = rows.Count(r => r.Status == "Candidate"),
                    Waiting = rows.Count(r => r.Status is "Pending" or "NeedsEvidence"),
                    Advice = rows.Count(r => r.Status == "Unsupported"),
                    Excluded = rows.Count(r => r.Status == "Excluded"),
                },
                months,
                categories,
                merchants,
                currencies,
                years = history,
                missing,
                compare = new { PreviousYear = year - 1, Items = compare },
                // For "about how much less tax": the year's resident scale, when KuTax has it.
                rates = scale is null ? null : new { scale.Brackets, scale.MedicareLevy, scale.Source },
            });
        }).RequireAuthorization();
    }
}
