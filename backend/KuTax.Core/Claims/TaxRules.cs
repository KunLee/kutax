using System.Text.Json;
using KuTax.Core.Receipts;

namespace KuTax.Core.Claims;

/// <summary>
/// One immutable rule version for a category and financial year. Built-in versions are seeded; later versions come from
/// activated rule packs. Labels and conditions are columns; the calculation (parameters, checks, formula, worked
/// examples and evidence) is <see cref="DefinitionJson"/>, evaluated only by <see cref="RuleEngine"/>.
/// </summary>
public sealed class TaxRule
{
    public string Version { get; set; } = "";
    public int FinancialYearStart { get; set; }
    public string Category { get; set; } = "";
    public string Label { get; set; } = "";
    public string Section { get; set; } = "Other work-related expenses";
    public string PaperReference { get; set; } = "D5 E";
    public string SourceUrl { get; set; } = "";
    public string MappingUrl { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public string Conditions { get; set; } = "";
    /// <summary>What the owner confirms for each expense in this category; recorded as the allocation's basis.</summary>
    public string Declaration { get; set; } = "";
    /// <summary>Paper-return CLAIM TYPE codes for the label (D3, D4); empty when the label has none.</summary>
    public string[] ClaimTypes { get; set; } = [];
    /// <summary>Claimed in full: no work-use apportionment or reimbursement (gifts). Shapes the screens, not the maths.</summary>
    public bool WholeAmountOnly { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    /// <summary>The version used for new assessments of its year and category; superseded versions stay for history.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>The rule pack that introduced this version; null for built-in versions.</summary>
    public Guid? PackId { get; set; }
}

public sealed class TaxRuleReview
{
    public string RuleVersion { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTimeOffset ReviewedAt { get; set; }
    public string Note { get; set; } = "";
}

public sealed class EvidenceLink
{
    public Guid AllocationId { get; set; }
    public Guid DocumentId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Every approval is append-only. Superseded decisions remain traceable.
public sealed class TaxClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllocationId { get; set; }
    public string OwnerId { get; set; } = "";
    public int FinancialYearStart { get; set; }
    public string RuleVersion { get; set; } = "";
    public Guid AllocationVersion { get; set; }
    public Guid TransactionVersion { get; set; }
    public decimal DeductibleAmount { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public string ActorId { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class TaxRuleCatalog
{
    public const string Conditions = "Ordinary periodic fee for current employment only. Paid and incurred within this financial year, with service entirely within the year; written evidence retained. Excludes joining fees, levies, capital costs, unrelated memberships and prepayments for another year. Reconcile payroll and myTax prefill to avoid double claiming. This is deliberately limited coverage.";
    public const string Coverage = "KuTax calculates only its reviewed categories, for financial years with an active rule set, from receipts the owner confirmed and approved. Car, travel, working-from-home, decline in value of items over the immediate-deduction limit, and anything outside a category's conditions stay unassessed.";
    private const string Ato = "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/";
    private const string WorkRelated = Ato + "work-related-deductions/";
    private const string Tools = WorkRelated + "tools-computers-and-items-you-use-for-work/";
    public static string Instructions(int year) => $"https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.{year+1}&dl=Y";
    // SHA-256 of the individual tax return instructions as downloaded when each rule set was verified (see docs/tax-rules).
    private static string FeesSha(int year) => year==2025?"5EFF5D035035E7B0898B8B316A6CC1D6B616577E10157A9D97B1B5CD30A68BF5":"C4B8FA8BB372485CC8E66FF38CC62D1CF6FA2B12834380FDC88EEC3F6EE946CB";
    private static string InstructionsSha(int year) => year==2025?"07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3":"BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9";
    private const string FeeDeclaration = "I paid and incurred this periodic fee for my current employment in this financial year, kept written evidence, excluded private and reimbursed amounts, and checked it has no joining fee, levy or service for another year. I'll reconcile any payroll or myTax prefill so it isn't claimed twice.";
    /// <summary>GST-inclusive amount × work use, rounded to cents (midpoint away from zero), less reimbursement of the work portion.</summary>
    public const string WorkPortion = "round2(amount * workUsePercent / 100) - reimbursement";
    private static readonly RuleTest[] PortionTests = [
        new("Full work use",new(){["amount"]=440m,["workUsePercent"]=100m},null,"Candidate",440m),
        new("Rounded work portion less reimbursement",new(){["amount"]=123.45m,["workUsePercent"]=50m,["reimbursement"]=1m},null,"Candidate",60.73m),
        new("No work use",new(){["amount"]=100m,["workUsePercent"]=0m},null,"Excluded",0m)];
    private static RuleTest[] WithClaimType(string code)=>[..PortionTests.Select(t=>t with{ClaimType=code}),new("Claim type required",new(){["amount"]=100m},null,"NeedsReview",null)];
    private static readonly RuleCheck[] ItemLimit = [
        new("amount > maxItemCost","Unsupported","Items costing more than ${maxItemCost} decline in value over their effective life; KuTax doesn't calculate that."),
        new("workUsePercent <= workUseAbove","Unsupported","An immediate deduction needs more than {workUseAbove}% work use; otherwise the item declines in value, which KuTax doesn't calculate.")];
    private static Dictionary<string,decimal> ItemLimits()=>new(){["maxItemCost"]=300m,["workUseAbove"]=50m};
    private static readonly RuleTest[] ItemTests = [
        new("At the limit, mainly for work",new(){["amount"]=300m,["workUsePercent"]=60m},null,"Candidate",180m,ItemLimits()),
        new("Over the limit",new(){["amount"]=300.01m},null,"Unsupported",null,ItemLimits()),
        new("Half work use isn't mainly for work",new(){["amount"]=29.99m,["workUsePercent"]=50m},null,"Unsupported",null,ItemLimits()),
        new("Subscription at 80%",new(){["amount"]=29.99m,["workUsePercent"]=80m},null,"Candidate",23.99m,ItemLimits())];

    /// <summary>Built-in rules for one year, as a rule pack (the same format later years arrive in).</summary>
    public static RulePackDocument BuiltInPack(int year)
    {
        var instructions=new PackSource("instructions",Instructions(year),InstructionsSha(year));
        PackRule Rule(string category,string label,string section,string paper,string source,string conditions,string declaration,
            string[]? claimTypes=null,bool whole=false,Dictionary<string,decimal>? parameters=null,RuleCheck[]? checks=null,string deductible=WorkPortion,RuleTest[]? tests=null,RuleEvidence[]? evidence=null)=>
            new(category,label,section,paper,claimTypes??[],whole,conditions,declaration,source,parameters??[],checks??[],deductible,tests??PortionTests,evidence??[]);
        var rules=new List<PackRule>();
        foreach(var (category,label) in new[]{("UnionFees","Union fees"),("ProfessionalMembership","Professional memberships")})
            rules.Add(Rule(category,label,"Other work-related expenses","D5 E","https://www.ato.gov.au/law/view/print?DocID=TXR%2FTR20007%2FNAT%2FATO%2F00001",Conditions,FeeDeclaration,
                evidence:[new("paperReference","instructions","union fees and subscriptions to trade, business or professional associations")]));
        rules.Add(Rule("SelfEducation","Self-education","Work-related self-education expenses","D4 D",WorkRelated+"education-training-and-seminars/self-education-expenses",
            "Course fees, textbooks, stationery and student fees for study with a sufficient connection to your employment when the cost was incurred: it maintains or improves skills or knowledge your current job requires (claim type K), or is likely to increase income from your current job (I). Not claimable: study while not employed, study only generally related to your job or to get a new job, Commonwealth supported place fees paid upfront or with HECS-HELP, HELP/SFSS/SSL/AASL/VSL repayments, and reimbursed amounts. FEE-HELP and VET Student Loan tuition can qualify. Travel, car and decline-in-value self-education costs are not calculated.",
            "I was employed when I paid this. The study maintains or improves skills or knowledge my current job requires, or is likely to increase my income from it — not only generally related, and not to get a new job. It isn't a Commonwealth supported place fee (paid upfront or with HECS-HELP) or a HELP repayment, and private and reimbursed amounts are excluded.",
            claimTypes:["K","I"],tests:WithClaimType("K"),evidence:[new("claimTypes","instructions","Select the code, K or I, that best describes your reason for incurring self-education expenses.")]));
        rules.Add(Rule("WorkClothing","Work clothing","Work-related clothing, laundry and dry-cleaning expenses","D3 C",WorkRelated+"clothes-and-items-you-wear-at-work/clothing-laundry-and-dry-cleaning-expenses",
            "Buying, renting, repairing or dry-cleaning clothing you wear for work that is protective (P), occupation-specific (S), a distinctive compulsory uniform (C) or a non-compulsory uniform your employer registered (N). Plain clothing, such as black trousers, white shirts or suits, is not claimable even when required. Use the code for the main type at this label. Laundry claimed at a per-load rate is not calculated.",
            "This is protective clothing, occupation-specific clothing, a distinctive compulsory uniform, or a registered non-compulsory uniform that I wear for work — not plain or everyday clothing. Private and reimbursed amounts are excluded.",
            claimTypes:["C","N","S","P"],tests:WithClaimType("P")));
        rules.Add(Rule("ToolsEquipment","Tools and equipment","Other work-related expenses","D5 E",Tools+"tools-and-equipment-to-perform-your-work",
            "Immediate deduction for the work-related portion of a tool, device or piece of equipment that cost $300 or less, used mainly (more than 50%) for work, and not part of a set or a number of identical items costing more than $300 together. Items over $300 decline in value over their effective life and are not calculated. Not claimable when your employer supplied or reimbursed the item.",
            "Each item cost $300 or less, isn't part of a set or identical items costing more than $300 together, and I use it more than half the time for my current job. The work-use percentage is from my records; private and reimbursed amounts are excluded.",
            parameters:new(){["maxItemCost"]=300m,["workUseAbove"]=50m},checks:ItemLimit,tests:ItemTests,
            evidence:[new("parameters.maxItemCost","instructions","an immediate deduction for the cost of depreciating assets costing $300 or less")]));
        rules.Add(Rule("SoftwareSubscriptions","Software and subscriptions","Other work-related expenses","D5 E",Tools+"computers-laptops-and-software",
            "Work-related portion of software, apps, subscriptions, reference books, technical journals and trade magazines costing $300 or less each, used mainly (more than 50%) for work, and not part of a set costing more than $300 together. Dearer items decline in value over their effective life and are not calculated.",
            "Each item cost $300 or less and isn't part of a set costing more than $300 together, and I use it more than half the time for my current job. The work-use percentage is from my records; private and reimbursed amounts are excluded.",
            parameters:new(){["maxItemCost"]=300m,["workUseAbove"]=50m},checks:ItemLimit,tests:ItemTests,
            evidence:[new("parameters.maxItemCost","instructions","an immediate deduction for the cost of depreciating assets costing $300 or less")]));
        rules.Add(Rule("PhoneInternet","Phone and internet","Other work-related expenses","D5 E",Tools+"mobile-phone-mobile-internet-and-other-devices",
            "Work-related portion of phone, mobile data and internet bills, worked out from your records (for example a representative 4-week diary and itemised bills). Not claimable if you use the fixed-rate method for working-from-home expenses, which already includes phone and internet usage. Handsets and devices are depreciating assets, not bills.",
            "This is a phone or internet bill. My work-use percentage comes from my records, such as a 4-week diary and itemised bills. I'm not using the working-from-home fixed-rate method this year, and private and reimbursed amounts are excluded.",
            tests:[..PortionTests,new("Work-use share of a bill",new(){["amount"]=100m,["workUsePercent"]=40m},null,"Candidate",40m)]));
        rules.Add(Rule("Donations","Gifts and donations","Gifts or donations","D9 J",Ato+"gifts-and-donations",
            "Gifts of money of $2 or more to an organisation with deductible gift recipient (DGR) status, where you received nothing in return (for example raffle tickets, dinners or merchandise). Gifts of property or shares, fundraising-event contributions and political donations are not calculated.",
            "This was a gift of $2 or more to an organisation that is a deductible gift recipient (DGR) — the receipt or ABN Lookup confirms it — and I received nothing in return.",
            whole:true,parameters:new(){["minAmount"]=2m},
            checks:[new("workUsePercent != 100 or reimbursement != 0","NeedsReview","Gifts are claimed in full: use 100% and no reimbursement."),new("amount < minAmount","Excluded","Gifts under ${minAmount} aren't deductible.")],
            deductible:"amount",
            tests:[new("Gift in full",new(){["amount"]=50m},null,"Candidate",50m),new("Below the minimum",new(){["amount"]=1.5m},null,"Excluded",null,new(){["minAmount"]=2m}),new("Apportioned gift",new(){["amount"]=50m,["workUsePercent"]=80m},null,"NeedsReview",null)],
            evidence:[new("parameters.minAmount","instructions","Did you make a gift or donation of $2 or more to an organisation that has a deductible gift recipient (DGR) status?")]));
        rules.Add(Rule("TaxAffairs","Managing tax affairs","Cost of managing tax affairs","D10 M",Ato+"cost-of-managing-tax-affairs",
            "Fees for preparing and lodging your return through a registered tax agent, tax advice from a recognised tax adviser, and tax reference material or software (apportioned if also used for other purposes). Penalties, advice from someone who isn't a recognised tax adviser, and financial advice not about your tax affairs are not claimable. Litigation costs (label L) are not calculated.",
            "This cost was for managing my own tax affairs — a registered tax agent, a recognised tax adviser, or tax reference material or software — and is not a penalty or general financial advice. Other uses and reimbursed amounts are excluded."));
        return new(RulePackDocument.CurrentSchema,"AU",year,Instructions(year),[instructions],[..rules]);
    }

    /// <summary>Turns a pack's rules into rule versions; built-in packs keep their original version names and source hashes.</summary>
    public static IEnumerable<TaxRule> ToRules(RulePackDocument pack,int version,Guid? packId,bool builtIn=false)=>pack.Rules.Select(r=>new TaxRule
    {
        Version=$"AU-{pack.FinancialYearStart}-{r.Category}-v{version}",FinancialYearStart=pack.FinancialYearStart,Category=r.Category,Label=r.Label,Section=r.Section,PaperReference=r.PaperReference,
        SourceUrl=r.SourceUrl,MappingUrl=pack.MappingUrl,
        SourceSha256=builtIn&&r.Category is "UnionFees" or "ProfessionalMembership"?FeesSha(pack.FinancialYearStart):pack.Sources.FirstOrDefault(s=>s.Url==pack.MappingUrl)?.Sha256??"",
        Conditions=r.Conditions,Declaration=r.Declaration,ClaimTypes=r.ClaimTypes,WholeAmountOnly=r.WholeAmountOnly,
        DefinitionJson=JsonSerializer.Serialize(new RuleDefinition(r.Parameters,r.Checks,r.Deductible,r.Tests,r.Evidence),RulePackDocument.Compact),PackId=packId,
    });

    /// <summary>Seeded rule versions (FY 2024–25 and 2025–26).</summary>
    public static readonly TaxRule[] Rules = [..new[]{2024,2025}.SelectMany(year=>ToRules(BuiltInPack(year),1,null,builtIn:true))];
    /// <summary>Categories KuTax knows how to collect; a pack may only define rules for categories the app can present.</summary>
    public static readonly string[] Categories = [..Rules.Select(r=>r.Category).Distinct(),"Unsupported","Excluded"];
    public static TaxRule? Find(int year,string category) => Rules.SingleOrDefault(r=>r.FinancialYearStart==year&&r.Category==category);

    /// <summary>The rule versions in a pack, turned back into pack form (for export, templates and comparisons).</summary>
    public static RulePackDocument ToPack(int year,IEnumerable<TaxRule> rules)
    {
        var list=rules.Where(r=>r.FinancialYearStart==year).OrderBy(r=>r.Category).ToList();
        var mapping=list.FirstOrDefault()?.MappingUrl??Instructions(year);
        return new(RulePackDocument.CurrentSchema,"AU",year,mapping,[new("instructions",mapping,list.FirstOrDefault(r=>r.Category is not("UnionFees" or "ProfessionalMembership"))?.SourceSha256)],
            [..list.Select(r=>{var d=RuleEngine.Definition(r);return new PackRule(r.Category,r.Label,r.Section,r.PaperReference,r.ClaimTypes,r.WholeAmountOnly,r.Conditions,r.Declaration,r.SourceUrl,d.Parameters,d.Checks,d.Deductible,d.Tests,d.Evidence);})]);
    }
}

/// <summary>The rule versions available to a request: active ones answer "which rule applies"; all of them resolve history.</summary>
public sealed class TaxRuleSet(IEnumerable<TaxRule> rules)
{
    private readonly TaxRule[] all=[..rules];
    public IReadOnlyList<TaxRule> All => all;
    public IEnumerable<TaxRule> Active => all.Where(r=>r.IsActive);
    public TaxRule? Find(int year,string category) => all.Where(r=>r.IsActive&&r.FinancialYearStart==year&&r.Category==category).MaxBy(r=>Revision(r.Version));
    /// <summary>The revision number at the end of a version name ("AU-2026-UnionFees-v3" → 3).</summary>
    public static int Revision(string version) => int.TryParse(version[(version.LastIndexOf("-v",StringComparison.Ordinal)+2)..],out var n)?n:0;
    public TaxRule? ByVersion(string? version) => version is null?null:all.FirstOrDefault(r=>r.Version==version);
    public IEnumerable<TaxRule> ForYear(int year) => Active.Where(r=>r.FinancialYearStart==year).OrderBy(r=>r.Category);
    /// <summary>Every category with a rule in any year, plus the two never calculated.</summary>
    public string[] Categories => [..all.Select(r=>r.Category).Distinct().Order(),"Unsupported","Excluded"];
    public static readonly TaxRuleSet BuiltIn = new(TaxRuleCatalog.Rules);
}

/// <summary>One myTax deduction label with its approved total and whole-dollar entry.</summary>
public sealed record MyTaxSection(string Section,string PaperReference,string? ClaimType,decimal ApprovedTotal,decimal EntryAmount,int Claims,string[] Categories);

public sealed record TaxAssessment(string Status, decimal? DeductibleAmount, string Reason, string? RuleVersion);
public static class TaxCalculator
{
    /// <summary>
    /// Assesses one allocation: owner decisions and evidence are checked here; the category's conditions and formula
    /// come from the rule version (<see cref="RuleEngine"/>).
    /// </summary>
    public static TaxAssessment Assess(ReceiptTransaction transaction,ExpenseAllocation allocation,TaxRule? rule,bool evidenceReady,bool ruleReviewed)
    {
        if(allocation.Category=="Excluded")return new("Excluded",null,"Explicitly excluded by the owner.",null);
        if(transaction.Date is null)return new("NeedsReview",null,"Confirm a receipt date to establish its financial year.",null);
        if(allocation.Category=="Unsupported")return new("Unsupported",null,"Kept for advice: KuTax doesn't calculate this kind of expense.",null);
        if(rule is null)return new("Unsupported",null,"This category or financial year has no verified KuTax rule.",null);
        if(!ruleReviewed)return new("UnverifiedRule",null,"Waiting for a one-time administrator check of this rule against the ATO sources; then it can be approved.",rule.Version);
        if(transaction.Status!="Confirmed"||!allocation.EligibilityConfirmed||string.IsNullOrWhiteSpace(allocation.Basis))return new("NeedsReview",null,"Confirm receipt facts, work connection and all limited-coverage conditions.",rule.Version);
        var inputs=new Dictionary<string,decimal>{["amount"]=allocation.Amount,["workUsePercent"]=allocation.WorkUsePercent,["reimbursement"]=allocation.WorkReimbursement,["receiptTotal"]=transaction.Amount??0};
        var outcome=RuleEngine.Evaluate(rule,inputs,allocation.ClaimType);
        if(outcome.Status!="Candidate"&&outcome.Status!="Excluded")return new(outcome.Status,null,outcome.Reason,rule.Version);
        if(outcome.Status=="Excluded"&&outcome.Deductible is null)return new("Excluded",null,outcome.Reason,rule.Version);
        if(!evidenceReady)return new("NeedsEvidence",null,"Original and supporting documents must be available and cleared for use.",rule.Version);
        var work=decimal.Round(allocation.Amount*allocation.WorkUsePercent/100m,2,MidpointRounding.AwayFromZero);
        if(allocation.Amount<=0||transaction.Amount is null||allocation.Amount>transaction.Amount||allocation.WorkUsePercent is <0 or >100||allocation.WorkReimbursement<0||allocation.WorkReimbursement>work)
            return new("NeedsReview",null,"Invalid allocation or reimbursement.",rule.Version);
        return new(outcome.Deductible==0?"Excluded":"Candidate",outcome.Deductible,outcome.Reason,rule.Version);
    }
    /// <summary>Convenience for tests and fixed-catalog callers: uses the built-in rule for the receipt's year.</summary>
    public static TaxAssessment Assess(ReceiptTransaction transaction,ExpenseAllocation allocation,bool evidenceReady,bool ruleReviewed)=>
        Assess(transaction,allocation,transaction.Date is {} date?TaxRuleSet.BuiltIn.Find(date.Year-(date.Month<7?1:0),allocation.Category):null,evidenceReady,ruleReviewed);
    // Aggregate cents first, then omit cents once for the whole-dollar category entry.
    public static decimal EntryAmount(IEnumerable<decimal> approved) => decimal.Truncate(approved.Sum());
    /// <summary>
    /// Approved claims grouped by myTax label. Each label omits cents once; where the label has a CLAIM TYPE box, the
    /// code of the largest approved amount is used ("the main type"), with the others still listed in the workbook.
    /// </summary>
    public static MyTaxSection[] Sections(IEnumerable<ClaimRow> rows,TaxRuleSet rules) => [..rows.Where(r=>r.Status=="Approved")
        .Select(r=>(Row:r,Rule:rules.ByVersion(r.RuleVersion)!)).GroupBy(x=>(x.Rule.Section,x.Rule.PaperReference))
        .Select(g=>{var total=g.Sum(x=>x.Row.DeductibleAmount??0);
            var main=g.Where(x=>x.Row.ClaimType is not null).GroupBy(x=>x.Row.ClaimType!).OrderByDescending(c=>c.Sum(x=>x.Row.DeductibleAmount??0)).ThenBy(c=>c.Key).Select(c=>c.Key).FirstOrDefault();
            return new MyTaxSection(g.Key.Section,g.Key.PaperReference,main,total,EntryAmount(g.Select(x=>x.Row.DeductibleAmount??0)),g.Count(),[..g.Select(x=>x.Row.Category).Distinct().Order()]);})
        .OrderBy(s=>int.Parse(s.PaperReference[1..s.PaperReference.IndexOf(' ')]))];
}
