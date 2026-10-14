using System.Globalization;
using System.Text;

namespace KuTax.Core.Claims;

/// <summary>One field of one rule compared with the rules in force, with its supporting quote when there is one.</summary>
public sealed record FieldChange(string Category,string Field,string? Previous,string? Proposed,bool Changed,string? Quote,string? Source,bool? QuoteVerified);
/// <summary>Everything an administrator needs to decide on a pack. Errors block activation; warnings need attention.</summary>
public sealed record PackReport(string[] Errors,string[] Warnings,FieldChange[] Fields,int ChangedFields,string ComparedWith)
{
    public bool CanActivate => Errors.Length==0;
}

public static class RulePackReview
{
    /// <summary>
    /// Validates a pack, verifies its quotes against the fetched source texts (when given) and compares it field by field
    /// with <paramref name="previous"/>. A quote counts only if it appears in its source, ignoring spacing and punctuation,
    /// and, for a number, only if the quote contains that number.
    /// </summary>
    public static PackReport Build(RulePackDocument pack,RulePackDocument? previous,IReadOnlyDictionary<string,string>? sourceTexts,IEnumerable<string>? extraWarnings=null,string comparedWith="")
    {
        var errors=RuleEngine.Validate(pack);
        var warnings=new List<string>(extraWarnings??[]);
        var normalised=sourceTexts?.ToDictionary(p=>p.Key,p=>Normalise(p.Value));
        var fields=new List<FieldChange>();
        var before=previous?.Rules.ToDictionary(r=>r.Category)??[];
        foreach(var rule in pack.Rules)
        {
            before.TryGetValue(rule.Category,out var old);
            if(old is null)warnings.Add($"{rule.Category} is new compared with {comparedWith}.");
            void Add(string field,string? was,string? now)
            {
                var evidence=rule.Evidence.FirstOrDefault(e=>e.Field==field);
                bool? verified=null;
                if(evidence is not null&&normalised is not null)
                {
                    verified=normalised.TryGetValue(evidence.Source,out var text)&&Normalise(evidence.Quote) is {Length:>=12} quote&&text.Contains(quote,StringComparison.Ordinal);
                    if(verified==true&&field.StartsWith("parameters.",StringComparison.Ordinal)&&now is not null&&!Normalise(evidence.Quote).Contains(Normalise(now),StringComparison.Ordinal))verified=false;
                    if(verified==false)errors.Add($"{rule.Category}: the quote for {field} wasn't found in the official source{(field.StartsWith("parameters.",StringComparison.Ordinal)?" with that number":"")}.");
                }
                fields.Add(new(rule.Category,field,was,now,old is null||was!=now,evidence?.Quote,evidence?.Source,verified));
            }
            Add("label",old?.Label,rule.Label);
            Add("section",old?.Section,rule.Section);
            Add("paperReference",old?.PaperReference,rule.PaperReference);
            Add("claimTypes",old is null?null:string.Join(", ",old.ClaimTypes),string.Join(", ",rule.ClaimTypes));
            foreach(var name in rule.Parameters.Keys.Union(old?.Parameters.Keys.AsEnumerable()??[]).Order())
                Add("parameters."+name,old is not null&&old.Parameters.TryGetValue(name,out var a)?Number(a):null,rule.Parameters.TryGetValue(name,out var b)?Number(b):null);
            Add("deductible",old?.Deductible,rule.Deductible);
            Add("checks",old is null?null:string.Join(" | ",old.Checks.Select(c=>$"{c.When} → {c.Status}")),string.Join(" | ",rule.Checks.Select(c=>$"{c.When} → {c.Status}")));
            Add("conditions",old?.Conditions,rule.Conditions);
            Add("declaration",old?.Declaration,rule.Declaration);
            // Every parameter in an AI draft needs a verified quote; imported packs show "not machine-checked" instead.
            if(normalised is not null)
                foreach(var name in rule.Parameters.Keys)
                    if(rule.Evidence.All(e=>e.Field!="parameters."+name))errors.Add($"{rule.Category}: {name} has no supporting quote.");
        }
        foreach(var removed in before.Keys.Except(pack.Rules.Select(r=>r.Category)))
            warnings.Add($"{removed} isn't in this pack; activating it stops KuTax calculating {removed} for FY {pack.FinancialYearStart}–{(pack.FinancialYearStart+1)%100:00}.");
        return new([..errors.Distinct()],[..warnings.Distinct()],[..fields],fields.Count(f=>f.Changed),comparedWith);
    }

    private static string Number(decimal value)=>value.ToString("0.##",CultureInfo.InvariantCulture);

    /// <summary>Lower-case letters and digits only, so PDF spacing, hyphenation and quote marks don't matter.</summary>
    public static string Normalise(string text)
    {
        var builder=new StringBuilder(text.Length);
        foreach(var c in text.Normalize(NormalizationForm.FormKC))if(char.IsLetterOrDigit(c))builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }
}
