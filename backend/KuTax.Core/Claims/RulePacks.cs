using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KuTax.Core.Claims;

/// <summary>A condition that stops a claim being calculated, checked in order before the deductible formula.</summary>
public sealed record RuleCheck(string When, string Status, string Reason);
/// <summary>
/// A worked example every rule must reproduce before it can be activated. <see cref="Parameters"/> pins the parameter
/// values the example was written for, so it checks the formula's logic even after a year's limits change.
/// </summary>
public sealed record RuleTest(string Name, Dictionary<string, decimal> Inputs, string? ClaimType, string ExpectStatus, decimal? ExpectDeductible, Dictionary<string, decimal>? Parameters = null);
/// <summary>An exact quote from an official source supporting one field of a rule.</summary>
public sealed record RuleEvidence(string Field, string Source, string Quote);
/// <summary>The calculation part of a rule, stored with each rule version.</summary>
public sealed record RuleDefinition(Dictionary<string, decimal> Parameters, RuleCheck[] Checks, string Deductible, RuleTest[] Tests, RuleEvidence[] Evidence);

public sealed record PackSource(string Id, string Url, string? Sha256);
public sealed record PackRule(string Category, string Label, string Section, string PaperReference, string[] ClaimTypes, bool WholeAmountOnly,
    string Conditions, string Declaration, string SourceUrl, Dictionary<string, decimal> Parameters, RuleCheck[] Checks, string Deductible, RuleTest[] Tests, RuleEvidence[] Evidence);
/// <summary>
/// One financial year of rules as data: labels, conditions, parameters and formulas. Packs are versioned per year;
/// an activated pack's rules become immutable rule versions. Calculation methods beyond this format need a release.
/// </summary>
public sealed record RulePackDocument(int SchemaVersion, string Jurisdiction, int FinancialYearStart, string MappingUrl, PackSource[] Sources, PackRule[] Rules)
{
    public const int CurrentSchema = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n", UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    /// <summary>For stored rule definitions: compact, so seeded data is identical on every platform.</summary>
    public static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
}

public sealed record RuleOutcome(string Status, decimal? Deductible, string Reason);

/// <summary>Validates rule packs and evaluates rule definitions; the only place formulas run.</summary>
public static partial class RuleEngine
{
    /// <summary>Values a formula can read from a claim. New inputs need a release, because KuTax has to collect them.</summary>
    public static readonly IReadOnlyDictionary<string, string> Inputs = new Dictionary<string, string>
    {
        ["amount"] = "Expense amount allocated from the receipt (AUD, GST included)",
        ["workUsePercent"] = "Work-use percentage, 0–100",
        ["reimbursement"] = "Reimbursement of the work portion (AUD)",
        ["receiptTotal"] = "Total of the whole receipt (AUD)",
    };
    public static readonly string[] CheckStatuses = ["Unsupported", "NeedsReview", "Excluded"];
    private static readonly ConcurrentDictionary<string, Compiled> Cache = new();

    private sealed record Compiled(RuleDefinition Definition, (Formula When, RuleCheck Check)[] Checks, Formula Deductible);

    public static RuleDefinition Definition(TaxRule rule) => Compile(rule.DefinitionJson).Definition;

    private static Compiled Compile(string json) => Cache.GetOrAdd(json, text =>
    {
        var definition = JsonSerializer.Deserialize<RuleDefinition>(text, RulePackDocument.Json) ?? throw new InvalidDataException("Empty rule definition.");
        var names = Names(definition.Parameters);
        return new(definition, [.. definition.Checks.Select(c => (Formula.Parse(c.When, names), c))], Formula.Parse(definition.Deductible, names));
    });

    private static HashSet<string> Names(Dictionary<string, decimal> parameters) => [.. Inputs.Keys, .. parameters.Keys];

    /// <summary>Applies a rule's claim-type requirement, checks and formula to one claim's values.</summary>
    public static RuleOutcome Evaluate(TaxRule rule, IReadOnlyDictionary<string, decimal> inputs, string? claimType) =>
        Evaluate(Compile(rule.DefinitionJson), rule.ClaimTypes, inputs, claimType);

    private static RuleOutcome Evaluate(Compiled compiled, string[] claimTypes, IReadOnlyDictionary<string, decimal> inputs, string? claimType, IReadOnlyDictionary<string, decimal>? parameters = null)
    {
        if (claimTypes.Length > 0 && !claimTypes.Contains(claimType)) return new("NeedsReview", null, $"Choose the claim type ({string.Join(", ", claimTypes)}) for this expense.");
        var values = new Dictionary<string, decimal>(compiled.Definition.Parameters);
        foreach (var (key, value) in parameters ?? new Dictionary<string, decimal>()) if (values.ContainsKey(key)) values[key] = value;
        foreach (var (key, value) in inputs) values[key] = value;
        try
        {
            foreach (var (when, check) in compiled.Checks)
                if (when.Condition(values)) return new(check.Status, null, Fill(check.Reason, compiled.Definition.Parameters));
            var deductible = compiled.Deductible.Number(values);
            if (deductible < 0) return new("NeedsReview", null, "The calculation came out negative; check the amounts.");
            return new(deductible == 0 ? "Excluded" : "Candidate", deductible, "Calculated by the reviewed rule: " + compiled.Deductible.Text + ".");
        }
        catch (Exception error) when (error is DivideByZeroException or KeyNotFoundException or OverflowException)
        {
            return new("NeedsReview", null, "The rule couldn't be calculated for these amounts.");
        }
    }

    /// <summary>Replaces {parameter} placeholders in a reason with the parameter's value.</summary>
    private static string Fill(string reason, Dictionary<string, decimal> parameters) =>
        Placeholder().Replace(reason, m => parameters.TryGetValue(m.Groups[1].Value, out var value) ? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : m.Value);

    /// <summary>Every problem that stops a pack being activated; empty when it is valid and all its examples pass.</summary>
    public static List<string> Validate(RulePackDocument pack)
    {
        var errors = new List<string>();
        if (pack.SchemaVersion != RulePackDocument.CurrentSchema) errors.Add($"Schema version {pack.SchemaVersion} isn't supported; this KuTax reads version {RulePackDocument.CurrentSchema}.");
        if (pack.Jurisdiction != "AU") errors.Add("Only Australian (AU) rule packs are supported.");
        if (pack.FinancialYearStart is < 2000 or > 2100) errors.Add("The financial year is out of range.");
        if (!OfficialUrl(pack.MappingUrl)) errors.Add("The year's mapping source must be an official ATO publication.");
        if (pack.Rules is not { Length: > 0 and <= 40 }) { errors.Add("A pack needs 1–40 rules."); return errors; }
        foreach (var duplicate in pack.Rules.GroupBy(r => r.Category).Where(g => g.Count() > 1)) errors.Add($"{duplicate.Key} appears more than once.");
        foreach (var rule in pack.Rules)
        {
            var at = $"{rule.Category}:";
            if (!CategoryName().IsMatch(rule.Category ?? "") || rule.Category is "Unsupported" or "Excluded" or "Personal") errors.Add($"{at} invalid category name.");
            if (!PaperReference().IsMatch(rule.PaperReference ?? "")) errors.Add($"{at} paper reference must look like \"D5 E\".");
            if (rule.ClaimTypes is null || rule.ClaimTypes.Any(c => !ClaimCode().IsMatch(c ?? "")) || rule.ClaimTypes.Distinct().Count() != rule.ClaimTypes.Length) errors.Add($"{at} claim types must be distinct single capital letters.");
            foreach (var (name, text, max) in new[] { ("label", rule.Label, 80), ("section", rule.Section, 120), ("conditions", rule.Conditions, 2000), ("declaration", rule.Declaration, 1000) })
                if (string.IsNullOrWhiteSpace(text) || text.Length > max) errors.Add($"{at} {name} must be 1–{max} characters.");
            if (!OfficialUrl(rule.SourceUrl)) errors.Add($"{at} source must be an official ATO page.");
            if (rule.Parameters is null || rule.Checks is null || rule.Tests is null || rule.Evidence is null) { errors.Add($"{at} parameters, checks, tests and evidence are required (they may be empty lists, except tests)."); continue; }
            foreach (var (name, value) in rule.Parameters)
            {
                if (!ParameterName().IsMatch(name) || Inputs.ContainsKey(name)) errors.Add($"{at} parameter name '{name}' is invalid or clashes with an input.");
                if (value is < -1_000_000 or > 1_000_000) errors.Add($"{at} parameter '{name}' is out of range.");
            }
            if (rule.Checks.Length > 20) errors.Add($"{at} at most 20 checks.");
            Compiled? compiled = null;
            try
            {
                var names = Names(rule.Parameters);
                var checks = new List<(Formula, RuleCheck)>();
                foreach (var check in rule.Checks)
                {
                    var when = Formula.Parse(check.When, names);
                    if (!when.IsCondition) errors.Add($"{at} check \"{check.When}\" must be a condition.");
                    if (!CheckStatuses.Contains(check.Status)) errors.Add($"{at} check status must be one of {string.Join(", ", CheckStatuses)}.");
                    if (string.IsNullOrWhiteSpace(check.Reason) || check.Reason.Length > 300) errors.Add($"{at} each check needs a reason of up to 300 characters.");
                    checks.Add((when, check));
                }
                var deductible = Formula.Parse(rule.Deductible, names);
                if (deductible.IsCondition) errors.Add($"{at} the deductible formula must be an amount.");
                if (!deductible.Names.Contains("amount")) errors.Add($"{at} the deductible formula must use the expense amount.");
                compiled = new(new(rule.Parameters, rule.Checks, rule.Deductible, rule.Tests, rule.Evidence), [.. checks], deductible);
            }
            catch (FormatException error) { errors.Add($"{at} {error.Message}"); }
            if (rule.Tests.Length is < 1 or > 30) errors.Add($"{at} needs 1–30 worked examples.");
            else if (compiled is not null && !errors.Any(e => e.StartsWith(at, StringComparison.Ordinal)))
                foreach (var test in rule.Tests)
                {
                    if (test.Inputs.Keys.Any(k => !Inputs.ContainsKey(k))) { errors.Add($"{at} example \"{test.Name}\" uses an unknown input."); continue; }
                    var inputs = new Dictionary<string, decimal> { ["amount"] = 0, ["workUsePercent"] = 100, ["reimbursement"] = 0 };
                    foreach (var (key, value) in test.Inputs) inputs[key] = value;
                    inputs.TryAdd("receiptTotal", inputs["amount"]);
                    if (test.Parameters?.Keys.Any(k => !rule.Parameters.ContainsKey(k)) == true) { errors.Add($"{at} example \"{test.Name}\" pins an unknown parameter."); continue; }
                    var outcome = Evaluate(compiled, rule.ClaimTypes ?? [], inputs, test.ClaimType, test.Parameters);
                    if (outcome.Status != test.ExpectStatus || outcome.Deductible != test.ExpectDeductible)
                        errors.Add($"{at} example \"{test.Name}\" expected {test.ExpectStatus} {test.ExpectDeductible?.ToString("0.00") ?? ""} but got {outcome.Status} {outcome.Deductible?.ToString("0.00") ?? ""}.".Replace("  ", " "));
                }
            foreach (var evidence in rule.Evidence)
                if (string.IsNullOrWhiteSpace(evidence.Field) || string.IsNullOrWhiteSpace(evidence.Quote) || evidence.Quote.Length > 600 || pack.Sources.All(s => s.Id != evidence.Source))
                    errors.Add($"{at} evidence for '{evidence.Field}' needs a quote of up to 600 characters from a listed source.");
        }
        foreach (var source in pack.Sources) if (!OfficialUrl(source.Url)) errors.Add($"Source '{source.Id}' must be an official ATO publication.");
        return errors;
    }

    public static bool OfficialUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && (uri.Host is "www.ato.gov.au" or "ato.gov.au" or "iorder.com.au");

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")] private static partial Regex Placeholder();
    [GeneratedRegex(@"^[A-Z][A-Za-z]{2,40}$")] private static partial Regex CategoryName();
    [GeneratedRegex(@"^D\d{1,2} [A-Z]$")] private static partial Regex PaperReference();
    [GeneratedRegex(@"^[A-Z]$")] private static partial Regex ClaimCode();
    [GeneratedRegex(@"^[a-z][A-Za-z0-9]{1,40}$")] private static partial Regex ParameterName();
}

/// <summary>
/// A rule pack moving through drafting, review and activation. AI drafts and imported packs both land here; nothing in a
/// pack is used until an administrator activates it, which creates new immutable rule versions.
/// </summary>
public sealed class RulePack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int FinancialYearStart { get; set; }
    /// <summary>Queued → Drafting → Draft (or Invalid / Failed) → Active, Rejected or Superseded.</summary>
    public string Status { get; set; } = "Queued";
    /// <summary>AiDraft or Imported.</summary>
    public string Origin { get; set; } = "AiDraft";
    public string PackJson { get; set; } = "{}";
    /// <summary>Validation errors, warnings, quote checks, disagreements between drafts, and the field-by-field comparison.</summary>
    public string ReportJson { get; set; } = "{}";
    public string? ErrorCode { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>When KuTax last looked for a financial year's official instructions, and whether they were found.</summary>
public sealed class RuleSourceCheck
{
    public int FinancialYearStart { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? Message { get; set; }
}
