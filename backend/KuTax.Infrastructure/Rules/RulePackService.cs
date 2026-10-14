using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Infrastructure.Rules;

/// <summary>Queues, drafts, imports and activates rule packs. Activation is the administrator's review of every rule in it.</summary>
public sealed class RulePackService(KuTaxDbContext db,IAtoSources sources,IAiOptionsSource ai)
{
    public static readonly string[] Open=["Queued","Drafting","Draft"];

    /// <summary>The first financial year without active rules after the latest year that has them.</summary>
    public async Task<int> NextYearAsync(CancellationToken ct)=>(await db.TaxRules.Where(r=>r.IsActive).MaxAsync(r=>(int?)r.FinancialYearStart,ct)??2024)+1;

    /// <summary>Looks for a year's instructions; when they're published and AI is set up, queues a draft (once).</summary>
    public async Task<RuleSourceCheck> CheckAsync(int year,string actor,CancellationToken ct)
    {
        var check=await db.RuleSourceChecks.FindAsync([year],ct);
        if(check is null){check=new(){FinancialYearStart=year};db.RuleSourceChecks.Add(check);}
        check.CheckedAt=DateTimeOffset.UtcNow;
        try
        {
            var published=await sources.InstructionsPublishedAsync(year,ct);
            if(published){check.PublishedAt??=DateTimeOffset.UtcNow;check.Message=null;}
            else check.Message=$"The ATO hasn't published the {year+1} instructions yet.";
        }
        catch(Exception error) when(error is HttpRequestException or TaskCanceledException){check.Message="The ATO publication site couldn't be reached.";}
        if(check.PublishedAt is not null&&AiPrivacy.Configured(await ai.GetAsync(ct))&&!await db.RulePacks.AnyAsync(p=>p.FinancialYearStart==year&&(Open.Contains(p.Status)||p.Status=="Active"),ct))
            db.RulePacks.Add(new(){FinancialYearStart=year,CreatedBy=actor});
        await db.SaveChangesAsync(ct);
        return check;
    }

    /// <summary>The rules a draft for <paramref name="year"/> starts from: the latest earlier year with active rules, or that year's own.</summary>
    public static RulePackDocument? Template(int year,TaxRuleSet rules)
    {
        var from=rules.Active.Where(r=>r.FinancialYearStart<year).Select(r=>(int?)r.FinancialYearStart).Max()??rules.Active.Where(r=>r.FinancialYearStart==year).Select(r=>(int?)r.FinancialYearStart).Max();
        return from is {} y?TaxRuleCatalog.ToPack(y,rules.Active):null;
    }
    /// <summary>What a pack is compared with: the year's current rules if it has any, otherwise the template.</summary>
    public static RulePackDocument? Baseline(int year,TaxRuleSet rules)=>rules.Active.Any(r=>r.FinancialYearStart==year)?TaxRuleCatalog.ToPack(year,rules.Active):Template(year,rules);

    public async Task<RulePack> ImportAsync(RulePackDocument document,string actor,CancellationToken ct)
    {
        var rules=await db.LoadRulesAsync(ct);var baseline=Baseline(document.FinancialYearStart,rules);
        var unknown=document.Rules.Select(r=>r.Category).Except(rules.Categories).ToArray();
        var warnings=new List<string>{"Imported pack: quotes weren't machine-checked against the sources. Check them yourself."};
        var report=RulePackReview.Build(document,baseline,null,warnings,baseline is null?"nothing (first rules)":$"FY {baseline.FinancialYearStart}–{(baseline.FinancialYearStart+1)%100:00} rules");
        var errors=report.Errors.Concat(unknown.Select(c=>$"{c}: KuTax can't present this category yet; new categories need a release.")).ToArray();
        var pack=new RulePack{FinancialYearStart=document.FinancialYearStart,Origin="Imported",Status="Draft",CreatedBy=actor,
            PackJson=JsonSerializer.Serialize(document,RulePackDocument.Json),ReportJson=JsonSerializer.Serialize(report with{Errors=errors},RulePackDocument.Json)};
        db.RulePacks.Add(pack);
        db.AuditEvents.Add(new(){OwnerId=actor,ActorId=actor,ResourceId=pack.Id,Action="RulePackImported",DetailsJson=JsonSerializer.Serialize(new{document.FinancialYearStart,rules=document.Rules.Length},ReceiptCandidate.Json)});
        await db.SaveChangesAsync(ct);
        return pack;
    }

    /// <summary>
    /// Turns a reviewed draft into new rule versions for its year. Earlier versions of that year become inactive but stay
    /// for history and frozen tax packs; existing approvals under them need re-approval. Records a review per new version.
    /// </summary>
    public async Task<string?> ActivateAsync(Guid id,string actor,string note,CancellationToken ct)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var pack=await db.RulePacks.SingleOrDefaultAsync(p=>p.Id==id,ct);
        if(pack is null)return "Not found.";
        if(pack.Status!="Draft")return "Only a draft can be activated.";
        var report=JsonSerializer.Deserialize<PackReport>(pack.ReportJson,RulePackDocument.Json)!;
        if(!report.CanActivate)return "Fix the errors in this pack first.";
        var document=JsonSerializer.Deserialize<RulePackDocument>(pack.PackJson,RulePackDocument.Json)!;
        if(RuleEngine.Validate(document) is {Count:>0} errors)return errors[0];
        var year=pack.FinancialYearStart;
        var existing=await db.TaxRules.Where(r=>r.FinancialYearStart==year).ToListAsync(ct);
        var revision=existing.Select(r=>TaxRuleSet.Revision(r.Version)).DefaultIfEmpty(0).Max()+1;
        foreach(var rule in existing.Where(r=>r.IsActive))rule.IsActive=false;
        await db.SaveChangesAsync(ct);
        var created=TaxRuleCatalog.ToRules(document,revision,pack.Id).ToList();
        db.TaxRules.AddRange(created);
        foreach(var rule in created)db.TaxRuleReviews.Add(new(){RuleVersion=rule.Version,ActorId=actor,ReviewedAt=DateTimeOffset.UtcNow,Note=note});
        foreach(var other in await db.RulePacks.Where(p=>p.FinancialYearStart==year&&p.Status=="Active").ToListAsync(ct))other.Status="Superseded";
        pack.Status="Active";pack.DecidedBy=actor;pack.DecidedAt=DateTimeOffset.UtcNow;pack.Note=note;
        db.AuditEvents.Add(new(){OwnerId=actor,ActorId=actor,ResourceId=pack.Id,Action="RulePackActivated",DetailsJson=JsonSerializer.Serialize(new{year,revision,versions=created.Select(r=>r.Version),note},ReceiptCandidate.Json)});
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }
}
