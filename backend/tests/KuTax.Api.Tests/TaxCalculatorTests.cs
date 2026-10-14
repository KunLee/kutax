using KuTax.Core.Claims;
using KuTax.Core.Receipts;

namespace KuTax.Api.Tests;
public sealed class TaxCalculatorTests
{
    [Theory]
    [InlineData(440,100,0,440)]
    [InlineData(123.45,50,1,60.73)]
    [InlineData(0.01,50,0,0.01)]
    [InlineData(100,0,0,0)]
    [InlineData(100,80,80,0)]
    public void Verified_fee_calculation_preserves_gst_and_rounds_work_portion(decimal expense,decimal use,decimal reimbursement,decimal expected)
    {
        var result=TaxCalculator.Assess(Transaction(),Allocation(expense,use,reimbursement),true,true);
        Assert.Equal(expected,result.DeductibleAmount);
        Assert.Equal(expected==0?"Excluded":"Candidate",result.Status);
    }
    [Fact]
    public void All_approval_conditions_are_required_and_future_rules_are_not_assumed()
    {
        var t=Transaction();var a=Allocation(100,100,0);
        Assert.Equal("UnverifiedRule",TaxCalculator.Assess(t,a,true,false).Status);
        Assert.Equal("NeedsEvidence",TaxCalculator.Assess(t,a,false,true).Status);
        a.EligibilityConfirmed=false;Assert.Equal("NeedsReview",TaxCalculator.Assess(t,a,true,true).Status);a.EligibilityConfirmed=true;
        t.Status="Draft";Assert.Equal("NeedsReview",TaxCalculator.Assess(t,a,true,true).Status);t.Status="Confirmed";
        t.Date=new(2026,7,1);Assert.Equal("Unsupported",TaxCalculator.Assess(t,a,true,true).Status);
        t.Date=new(2026,6,30);Assert.Equal("Candidate",TaxCalculator.Assess(t,a,true,true).Status);
        a.Category="Unsupported";Assert.Null(TaxCalculator.Assess(t,a,true,true).DeductibleAmount);
    }
    [Fact]
    public void Entry_aggregates_before_omitting_cents()=>Assert.Equal(2m,TaxCalculator.EntryAmount([1.60m,0.60m]));
    [Fact]
    public void Category_conditions_decide_what_is_calculated()
    {
        var t=Transaction();
        ExpenseAllocation A(string category,decimal amount,decimal use=100,string? claimType=null,decimal reimbursed=0)=>new(){Category=category,Amount=amount,WorkUsePercent=use,WorkReimbursement=reimbursed,ClaimType=claimType,Basis="Synthetic",EligibilityConfirmed=true};
        Assert.Equal("NeedsReview",TaxCalculator.Assess(t,A("SelfEducation",500),true,true).Status);
        Assert.Equal(("Candidate",500m),(TaxCalculator.Assess(t,A("SelfEducation",500,claimType:"K"),true,true).Status,TaxCalculator.Assess(t,A("SelfEducation",500,claimType:"K"),true,true).DeductibleAmount));
        Assert.Equal("NeedsReview",TaxCalculator.Assess(t,A("WorkClothing",80,claimType:"K"),true,true).Status);
        Assert.Equal("Candidate",TaxCalculator.Assess(t,A("WorkClothing",80,claimType:"P"),true,true).Status);
        Assert.Equal("Candidate",TaxCalculator.Assess(t,A("ToolsEquipment",300,60),true,true).Status);
        Assert.Equal("Unsupported",TaxCalculator.Assess(t,A("ToolsEquipment",300.01m,100),true,true).Status);
        Assert.Equal("Unsupported",TaxCalculator.Assess(t,A("SoftwareSubscriptions",29.99m,50),true,true).Status); // needs more than 50% work use
        Assert.Equal(40.00m,TaxCalculator.Assess(t,A("PhoneInternet",100,40),true,true).DeductibleAmount);
        Assert.Equal("NeedsReview",TaxCalculator.Assess(t,A("Donations",50,80),true,true).Status);
        Assert.Equal("Excluded",TaxCalculator.Assess(t,A("Donations",1.50m),true,true).Status);
        Assert.Equal(50m,TaxCalculator.Assess(t,A("Donations",50),true,true).DeductibleAmount);
        Assert.Equal(220m,TaxCalculator.Assess(t,A("TaxAffairs",220),true,true).DeductibleAmount);
        t.Date=new(2026,7,1);Assert.Equal("Unsupported",TaxCalculator.Assess(t,A("TaxAffairs",220),true,true).Status);
    }
    [Fact]
    public void Each_rule_year_is_sourced_and_every_label_is_a_verified_paper_reference()
    {
        Assert.Equal(18,TaxRuleCatalog.Rules.Length);
        Assert.All(TaxRuleCatalog.Rules,r=>{Assert.StartsWith("https://",r.SourceUrl);Assert.Equal(64,r.SourceSha256.Length);Assert.NotEmpty(r.Declaration);Assert.NotEmpty(r.Label);
            Assert.Contains(r.PaperReference,new[]{"D3 C","D4 D","D5 E","D9 J","D10 M"});});
        Assert.Equal(TaxRuleCatalog.Rules.Length,TaxRuleCatalog.Rules.Select(r=>r.Version).Distinct().Count());
    }
    [Fact]
    public void Labels_drop_cents_once_each_and_use_the_largest_claim_type()
    {
        ClaimRow Row(string category,decimal amount,string? claimType=null,string status="Approved")=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"M",new(2025,8,1),amount,"d",category,amount,100,0,"b",status,amount,"r",$"AU-2025-{category}-v1",null,null,null,[],ClaimType:claimType);
        var sections=TaxCalculator.Sections([Row("SelfEducation",100.60m,"I"),Row("SelfEducation",60.70m,"K"),Row("SelfEducation",50.50m,"K"),Row("UnionFees",1.60m),Row("SoftwareSubscriptions",0.60m),Row("Donations",10m,status:"Candidate")],TaxRuleSet.BuiltIn);
        Assert.Equal(new[]{"D4 D","D5 E"},sections.Select(s=>s.PaperReference));
        Assert.Equal(("K",211.80m,211m),(sections[0].ClaimType,sections[0].ApprovedTotal,sections[0].EntryAmount));
        Assert.Equal((2.20m,2m,(string?)null),(sections[1].ApprovedTotal,sections[1].EntryAmount,sections[1].ClaimType));
    }
    private static ReceiptTransaction Transaction()=>new(){Date=new(2025,8,1),Amount=1000,Status="Confirmed"};
    private static ExpenseAllocation Allocation(decimal expense,decimal use,decimal reimbursement)=>new(){Category="UnionFees",Amount=expense,WorkUsePercent=use,WorkReimbursement=reimbursement,Basis="Current employment periodic fee in this year",EligibilityConfirmed=true};
}

public sealed class AllocationValidationTests
{
    private static KuTax.Api.Features.Receipts.AllocationInput Item(string category="Unsupported",decimal amount=34m,decimal use=100m,decimal reimbursed=0m,string? basis=null,bool confirmed=false,string? claimType=null,string description="Anthropic")=>
        new(description,category,amount,use,reimbursed,basis,confirmed,claimType);

    [Fact]
    public void Problems_name_the_field_and_expense_and_notes_are_optional()
    {
        var rules=TaxRuleSet.BuiltIn;
        string? Problem(params KuTax.Api.Features.Receipts.AllocationInput[] items)=>KuTax.Api.Features.Receipts.AllocationEndpoints.Problem(items,34m,rules);
        Assert.Null(Problem(Item()));                                     // no notes needed
        Assert.Equal("choose a category.",Problem(Item(category:"Mystery")));
        Assert.Equal("Expense 2: add a short description.",Problem(Item(amount:10m),Item(amount:10m,description:" ")));
        Assert.Equal("the reimbursement can't be more than the work portion ($17.00).",Problem(Item(use:50m,reimbursed:20m)));
        Assert.Equal("The expenses add up to $40.00, more than the receipt total of $34.00.",Problem(Item(amount:20m),Item(amount:20m)));
        Assert.Equal("that claim type doesn't apply to this category.",Problem(Item(category:"SoftwareSubscriptions",claimType:"K")));
        var rule=rules.Find(2025,"SoftwareSubscriptions");
        Assert.StartsWith("Confirmed: Each item cost $300 or less",KuTax.Api.Features.Receipts.AllocationEndpoints.Basis(Item(category:"SoftwareSubscriptions",confirmed:true),rule));
        Assert.Equal("Kept for advice: KuTax doesn't calculate this kind of expense.",KuTax.Api.Features.Receipts.AllocationEndpoints.Basis(Item(),null));
        Assert.Equal("My own words",KuTax.Api.Features.Receipts.AllocationEndpoints.Basis(Item(basis:" My own words "),null));
    }
}
