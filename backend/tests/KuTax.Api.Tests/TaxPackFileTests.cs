using System.Text;
using ClosedXML.Excel;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Reports;
using UglyToad.PdfPig;

namespace KuTax.Api.Tests;
public sealed class TaxPackFileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class MemoryStorage : IDocumentStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) { using var copy = new MemoryStream(); await content.CopyToAsync(copy, ct); Objects[key] = copy.ToArray(); }
        public Task<StoredObject> OpenReadAsync(string key, CancellationToken ct) { var bytes = Objects[key]; return Task.FromResult(new StoredObject(new MemoryStream(bytes), bytes.Length)); }
        public Task DeleteAsync(string key, CancellationToken ct) { Objects.Remove(key); return Task.CompletedTask; }
    }

    /// <summary>A synthetic FY 2025–26 pack: self-education (D4, K), two software claims and an unclaimed item.</summary>
    private static (AnnualTaxSnapshot Snapshot, MemoryStorage Storage) Pack()
    {
        var storage = new MemoryStorage();
        EvidenceReference Doc(string name) { var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 synthetic " + name); var key = "doc/" + name; storage.Objects[key] = bytes; return new(Guid.NewGuid(), name, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length, key, "application/pdf"); }
        var uni = Doc("uni-invoice.pdf"); var app = Doc("cursor.pdf"); var phone = Doc("telstra.pdf"); var cafe = Doc("cafe.pdf");
        ClaimRow Row(EvidenceReference doc, string merchant, string description, string category, decimal amount, decimal use, string status, decimal? deductible, string? claimType, DateOnly date) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), doc.Id, merchant, date, amount, description, category, amount, use, 0, "Synthetic basis", status, deductible, "Synthetic reason",
                status is "Approved" or "Candidate" ? $"AU-2025-{category}-v1" : null, status == "Approved" ? Guid.NewGuid() : null, status == "Approved" ? "owner" : null, status == "Approved" ? DateTimeOffset.UtcNow : null, [doc], ClaimType: claimType);

        var rowList = new[] {
            Row(uni, "University of Sydney", "University of Sydney", "SelfEducation", 5211.14m, 100, "Approved", 5211.14m, "K", new(2026, 4, 14)),
            Row(app, "Anysphere, Inc.", "Cursor Pro", "SoftwareSubscriptions", 29.99m, 80, "Approved", 23.99m, null, new(2025, 8, 9)) with { OriginalCurrency = "USD", OriginalAmount = 19.50m, ConversionSource = "Statement" },
            Row(phone, "Telstra", "Telstra", "SoftwareSubscriptions", 34.00m, 100, "Approved", 34.00m, null, new(2026, 3, 22)),
            Row(cafe, "Synthetic Cafe", "Synthetic Cafe", "Unsupported", 14.50m, 100, "Unsupported", null, null, new(2025, 9, 1)),
        };
        var rules = new TaxRuleSet(TaxRuleCatalog.Rules);
        var sections = TaxCalculator.Sections(rowList, rules);
        var approved = rowList.Where(r => r.Status == "Approved").Sum(r => r.DeductibleAmount!.Value);
        var assessment = new YearAssessment(2025, rowList, [new(cafe.Id, "Unallocated receipt balance: Synthetic Cafe", 1m)], approved, 0, 0, 14.50m, sections.Sum(s => s.EntryAmount), sections);
        var signoffs = rules.ForYear(2025).Select(r => new RuleSignoff(r, "admin", DateTimeOffset.UtcNow, "Synthetic review")).ToArray();
        return (new AnnualTaxSnapshot(Guid.NewGuid(), "annual-v1", 2025, "owner", DateTimeOffset.UtcNow, "Synthetic note", assessment, signoffs, [uni, app, phone, cafe]), storage);
    }

    [Fact]
    public async Task Packs_have_a_one_page_summary_an_item_by_item_guide_and_a_simple_spreadsheet_in_english_and_chinese()
    {
        var (snapshot, storage) = Pack();
        var files = await new AnnualReportGenerator(storage).GenerateAsync(snapshot, "SYNTHETICHASH", Ct);
        Assert.Equal(["summary", "guide", "workbook", "summary-zh", "guide-zh", "workbook-zh", "evidence"], files.Select(f => f.Kind));
        var output = Path.Combine(Path.GetTempPath(), "kutax-pack-check"); Directory.CreateDirectory(output);
        foreach (var file in files) File.WriteAllBytes(Path.Combine(output, file.FileName), storage.Objects[file.ObjectKey]);

        // Compared without whitespace: PDF text extraction doesn't keep spaces or zero-width break points reliably.
        static string Flat(string value) => new(value.Where(c => !char.IsWhiteSpace(c) && c != '​').ToArray());
        string Text(string kind) { using var pdf = PdfDocument.Open(storage.Objects[files.Single(f => f.Kind == kind).ObjectKey]); return Flat(string.Join("", pdf.GetPages().Select(p => p.Text))); }
        int Pages(string kind) { using var pdf = PdfDocument.Open(storage.Objects[files.Single(f => f.Kind == kind).ObjectKey]); return pdf.NumberOfPages; }
        var summary = Text("summary");
        Assert.Equal(1, Pages("summary"));
        Assert.Contains("$5,269.13", summary); // approved total
        Assert.Contains("$5,211", summary); Assert.Contains("$57", summary); // whole dollars per section (D4, D5)
        Assert.DoesNotContain("SYNTHETICHASH", summary); // no technical detail in the readable files

        var guide = Text("guide");
        foreach (var expected in new[] { "Anysphere, Inc. – Cursor Pro (80% work use)", "Telstra – Software and subscriptions", "University of Sydney – Self-education", "Anysphere, Inc._2025-08-09_USD 19.50_Software and subscriptions.pdf", "$23.99", "K – maintains" })
            Assert.Contains(Flat(expected), guide);
        Assert.DoesNotContain("SyntheticCafe", guide); // only approved claims are entered

        var chinese = Text("summary-zh");
        Assert.Contains(Flat("可申报的抵扣总额"), chinese);
        Assert.Contains(Flat("Work-related self-education expenses (D4 D) — 与工作相关的自我教育费用"), chinese);
        Assert.Contains(Flat("在 myTax 中输入的描述"), Text("guide-zh"));
        Assert.Contains(Flat("Anysphere, Inc. – Cursor Pro"), Text("guide-zh")); // what to type stays in English

        using var workbook = new XLWorkbook(new MemoryStream(storage.Objects[files.Single(f => f.Kind == "workbook").ObjectKey]));
        Assert.Equal(["Summary", "myTax entries", "Not claimed", "All expenses", "Rules", "Evidence"], workbook.Worksheets.Select(w => w.Name));
        Assert.Equal(5269.13m, workbook.Worksheet("Summary").Cell(2, 2).GetValue<decimal>());
        var entries = workbook.Worksheet("myTax entries");
        Assert.Equal(3, entries.RowsUsed().Count(r => r.RowNumber() > 1 && r.Cell(4).GetString().Length > 0));
        Assert.Equal(5269.13m, workbook.Worksheet("All expenses").RowsUsed().Skip(1).Where(r => r.Cell(10).GetString() == "Approved").Sum(r => r.Cell(9).GetValue<decimal>()));
        // Nothing technical: no IDs, versions or checksums anywhere in the spreadsheet.
        Assert.DoesNotContain(workbook.Worksheets.SelectMany(w => w.CellsUsed()), c => c.GetString().Contains(snapshot.Assessment.Rows[0].AllocationId.ToString()) || c.GetString().Contains("SHA") || c.GetString().Contains(snapshot.Evidence[0].Sha256) || c.GetString().Contains("-v1"));
        Assert.Contains("Synthetic Cafe", workbook.Worksheet("Not claimed").Column(2).CellsUsed().Select(c => c.GetString()));
        Assert.All(workbook.Worksheets.SelectMany(s => s.CellsUsed()), cell => Assert.False(cell.HasFormula));
        // Receipts in the ZIP are named merchant_date_invoice total, matching the guide's receipt column.
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(storage.Objects[files.Single(f => f.Kind == "evidence").ObjectKey]));
        Assert.Equal(["originals/Anysphere, Inc._2025-08-09_USD 19.50_Software and subscriptions.pdf", "originals/Synthetic Cafe_2025-09-01_AUD 14.50_Needs advice.pdf", "originals/Telstra_2026-03-22_AUD 34.00_Software and subscriptions.pdf", "originals/University of Sydney_2026-04-14_AUD 5211.14_Self-education.pdf"],
            zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("originals/")).Order(StringComparer.Ordinal));
        using var chineseBook = new XLWorkbook(new MemoryStream(storage.Objects[files.Single(f => f.Kind == "workbook-zh").ObjectKey]));
        Assert.Equal("摘要", chineseBook.Worksheets.First().Name);
    }
}
