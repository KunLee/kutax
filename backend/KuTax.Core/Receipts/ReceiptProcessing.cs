using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuTax.Core.Receipts;
public sealed class ProcessingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? ErrorCode { get; set; }
}
public sealed class ExtractionAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string SchemaVersion { get; set; } = "receipt-v1";
    public string CandidateJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ReceiptTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string? Merchant { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Gst { get; set; }
    public string Currency { get; set; } = "AUD";
    /// <summary>For a receipt in another currency: what was shown on it, and how the AUD amount was arrived at.</summary>
    public string? OriginalCurrency { get; set; }
    public decimal? OriginalAmount { get; set; }
    /// <summary>"Statement" (AUD from the person's bank or card statement) or "RBA" (estimate at the RBA daily rate).</summary>
    public string? ConversionSource { get; set; }
    /// <summary>RBA rate as published: units of the foreign currency per A$1.</summary>
    public decimal? ConversionRate { get; set; }
    public DateOnly? ConversionRateDate { get; set; }
    /// <summary>AI suggestion of the expense category, for the person to accept or change. Never used to calculate.</summary>
    public string? SuggestedCategory { get; set; }
    public string Status { get; set; } = "Draft";
    /// <summary>Why a skipped ("Dismissed") receipt isn't for the tax return: Personal, NotDeductible, Duplicate, OtherYear or Other.</summary>
    public string? SkipReason { get; set; }
    public static readonly string[] SkipReasons = ["Personal","NotDeductible","Duplicate","OtherYear","Other"];
    public string Notes { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset? ConfirmedAt { get; set; }
}
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid ResourceId { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class PrivacyPreference
{
    public string OwnerId { get; set; } = "";
    public bool ExternalAiConsent { get; set; }
    public string NoticeVersion { get; set; } = "ai-privacy-v1";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record ExtractedField<T>(T? Value, decimal Confidence, string? Evidence);
public sealed record ReceiptCandidate(ExtractedField<string> Merchant, ExtractedField<string> Date,
    ExtractedField<decimal?> Amount, ExtractedField<decimal?> Gst, string Currency,
    ExtractedField<string>? OriginalCurrency = null, ExtractedField<decimal?>? OriginalAmount = null, ExtractedField<string>? Category = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static ReceiptCandidate Parse(string json)
    {
        var result = JsonSerializer.Deserialize<ReceiptCandidate>(json, Json) ?? throw new InvalidDataException("Empty extraction.");
        if (result.Merchant is null || result.Date is null || result.Amount is null || result.Gst is null
            || result.Currency != "AUD" || result.Merchant.Value?.Length > 200 || result.Date.Value is not null && !DateOnly.TryParseExact(result.Date.Value,"yyyy-MM-dd",out _)
            || result.Amount.Value is < 0 or > 1000000 || result.Amount.Value.HasValue&&decimal.Round(result.Amount.Value.Value,2)!=result.Amount.Value
            || result.Gst.Value is < 0 || result.Gst.Value.HasValue&&(!result.Amount.Value.HasValue||decimal.Round(result.Gst.Value.Value,2)!=result.Gst.Value) || result.Gst.Value > result.Amount.Value
            || !Valid(result.Merchant.Confidence,result.Merchant.Evidence,result.Merchant.Value is not null)
            || !Valid(result.Date.Confidence,result.Date.Evidence,result.Date.Value is not null)
            || !Valid(result.Amount.Confidence,result.Amount.Evidence,result.Amount.Value is not null)
            || !Valid(result.Gst.Confidence,result.Gst.Evidence,result.Gst.Value is not null)) throw new InvalidDataException("Invalid receipt extraction.");
        return result;
    }
    /// <summary>
    /// Like <see cref="Parse"/>, but a field that fails validation is blanked (with a note) instead of discarding
    /// the whole reading. Malformed or structurally wrong payloads are still rejected outright.
    /// </summary>
    public static ReceiptCandidate Sanitize(string json, DateOnly today)
    {
        var result = JsonSerializer.Deserialize<ReceiptCandidate>(json, Json) ?? throw new InvalidDataException("Empty extraction.");
        if (result.Merchant is null || result.Date is null || result.Amount is null || result.Gst is null || result.Currency != "AUD")
            throw new InvalidDataException("Invalid receipt extraction.");
        static ExtractedField<T> Blank<T>(string why) => new(default, 0, "Could not be verified (" + why + "); enter it from the original.");
        var merchant = result.Merchant.Value?.Length > 200 || !Valid(result.Merchant.Confidence, result.Merchant.Evidence, result.Merchant.Value is not null)
            ? Blank<string>("merchant") : result.Merchant;
        var date = result.Date.Value is not null && (!DateOnly.TryParseExact(result.Date.Value, "yyyy-MM-dd", out var parsed) || parsed.Year < 2000 || parsed > today.AddDays(1))
            || !Valid(result.Date.Confidence, result.Date.Evidence, result.Date.Value is not null) ? Blank<string>("date") : result.Date;
        var amountOk = !(result.Amount.Value is <= 0 or > 1000000 || result.Amount.Value.HasValue && decimal.Round(result.Amount.Value.Value, 2) != result.Amount.Value
            || !Valid(result.Amount.Confidence, result.Amount.Evidence, result.Amount.Value is not null));
        var amount = amountOk ? result.Amount : Blank<decimal?>("amount");
        var gst = result.Gst.Value is < 0 || result.Gst.Value.HasValue && (!amountOk || !amount.Value.HasValue || decimal.Round(result.Gst.Value.Value, 2) != result.Gst.Value || result.Gst.Value > amount.Value)
            || !Valid(result.Gst.Confidence, result.Gst.Evidence, result.Gst.Value is not null) ? Blank<decimal?>("GST") : result.Gst;
        // A receipt in another currency: keep what it shows, so the person can convert it properly.
        var code = result.OriginalCurrency;
        var codeOk = code is { Value: { } shown } && ForeignCurrency.IsCode(shown) && Valid(code.Confidence, code.Evidence, true);
        var original = result.OriginalAmount;
        var originalOk = codeOk && original is { Value: > 0 and <= ForeignCurrency.MaxAmount } && decimal.Round(original.Value!.Value, 2) == original.Value
            && Valid(original.Confidence, original.Evidence, true);
        // A category suggestion is only a hint; anything outside the known list is dropped.
        var category = result.Category is { Value: { } kind } && SuggestableCategories.Contains(kind) && Valid(result.Category.Confidence, result.Category.Evidence, true) ? result.Category : null;
        return new(merchant, date, amount, gst, "AUD", codeOk ? code : null, originalOk ? original : null, category);
    }
    /// <summary>Categories AI may suggest: the calculated ones, "Unsupported" (possibly work-related, needs advice) and "Personal".</summary>
    public static readonly string[] SuggestableCategories = ["UnionFees","ProfessionalMembership","SelfEducation","WorkClothing","ToolsEquipment","SoftwareSubscriptions","PhoneInternet","Donations","TaxAffairs","Unsupported","Personal"];
    private static bool Valid(decimal confidence,string? evidence,bool hasValue) => confidence is >=0 and <=1 && (evidence is null || evidence.Length <=1000) && (!hasValue || !string.IsNullOrWhiteSpace(evidence));
    public static ReceiptCandidate Empty => new(new(null,0,"Not extracted; enter manually."),new(null,0,"Not extracted; enter manually."),new(null,0,"Not extracted; enter manually."),new(null,0,"Not extracted; enter manually."),"AUD");
}
public interface IReceiptExtractor
{
    Task<(ReceiptCandidate Candidate,string Provider,string Model)> ExtractAsync(ReceiptDocument document, Stream content,bool consent,CancellationToken ct);
}

/// <summary>Suggests expense categories from merchant names alone, for receipts read before categories were suggested.</summary>
public interface ICategorySuggester
{
    /// <returns>A suggestion per input index (missing when unsure), or null when AI reading isn't available.</returns>
    Task<IReadOnlyDictionary<int,string>?> SuggestAsync(IReadOnlyList<string> merchants,CancellationToken ct);
}
