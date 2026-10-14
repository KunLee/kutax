using KuTax.Core.Claims;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Claims;

public static class AnnualAssessment
{
    public static async Task<YearAssessment> Load(KuTaxDbContext db,string owner,int year,CancellationToken ct)
    {
        var from=new DateOnly(year,7,1);var to=from.AddYears(1);
        var transactions=await db.Transactions.AsNoTracking().Where(t=>t.OwnerId==owner&&t.Status!="Dismissed"&&t.Date>=from&&t.Date<to).ToListAsync(ct);
        var ids=transactions.Select(t=>t.Id).ToArray();
        var allocations=await db.Allocations.AsNoTracking().Where(a=>a.OwnerId==owner&&a.IsActive&&ids.Contains(a.TransactionId)).ToListAsync(ct);
        if(allocations.Count>10000)throw new InvalidOperationException("Annual allocation limit exceeded.");
        var allocationIds=allocations.Select(a=>a.Id).ToArray();
        var links=await db.EvidenceLinks.AsNoTracking().Where(e=>e.OwnerId==owner&&allocationIds.Contains(e.AllocationId)).ToListAsync(ct);
        var documentIds=transactions.Select(t=>t.DocumentId).Concat(links.Select(l=>l.DocumentId)).Distinct().ToArray();
        var documents=await db.Documents.AsNoTracking().Where(d=>d.OwnerId==owner&&documentIds.Contains(d.Id)).ToDictionaryAsync(d=>d.Id,ct);
        var ruleSet=await db.LoadRulesAsync(ct);
        var reviewed=(await db.TaxRuleReviews.AsNoTracking().Select(r=>r.RuleVersion).ToListAsync(ct)).ToHashSet();
        var claims=await db.TaxClaims.AsNoTracking().Where(c=>c.OwnerId==owner&&c.FinancialYearStart==year&&allocationIds.Contains(c.AllocationId)).ToListAsync(ct);
        var rows=new List<ClaimRow>();
        foreach(var allocation in allocations)
        {
            var transaction=transactions.Single(t=>t.Id==allocation.TransactionId);
            var evidenceIds=links.Where(l=>l.AllocationId==allocation.Id).Select(l=>l.DocumentId).Append(transaction.DocumentId).Distinct().Order().ToArray();
            var ready=evidenceIds.All(id=>documents.TryGetValue(id,out var d)&&d.ScanStatus=="Clean"&&d.Status!="Quarantined");
            var rule=ruleSet.Find(year,allocation.Category);
            var assessment=TaxCalculator.Assess(transaction,allocation,rule,ready,rule is not null&&reviewed.Contains(rule.Version));
            if(allocations.Where(a=>a.TransactionId==transaction.Id).Sum(a=>a.Amount)>(transaction.Amount??0))
                assessment=new("NeedsReview",null,"Allocated expenses exceed the corrected receipt total. Edit allocations before approving.",rule?.Version);
            var approved=claims.SingleOrDefault(c=>c.AllocationId==allocation.Id&&c.AllocationVersion==allocation.Version&&c.TransactionVersion==transaction.Version&&c.RuleVersion==assessment.RuleVersion&&c.DeductibleAmount==assessment.DeductibleAmount);
            var status=assessment.Status=="Candidate"&&approved is not null?"Approved":assessment.Status;
            var evidence=evidenceIds.Where(documents.ContainsKey).Select(id=>documents[id]).Select(d=>new EvidenceReference(d.Id,d.OriginalFileName,d.Sha256,d.Size,d.ObjectKey,d.ContentType)).ToArray();
            rows.Add(new(allocation.Id,allocation.Version,transaction.Id,transaction.Version,transaction.DocumentId,transaction.Merchant??"Unknown",transaction.Date!.Value,transaction.Amount??0,allocation.Description,allocation.Category,allocation.Amount,allocation.WorkUsePercent,allocation.WorkReimbursement,allocation.Basis,status,assessment.DeductibleAmount,assessment.Reason,assessment.RuleVersion,status=="Approved"?approved!.Id:null,status=="Approved"?approved!.ActorId:null,status=="Approved"?approved!.ApprovedAt:null,evidence,
                transaction.OriginalCurrency,transaction.OriginalAmount,transaction.ConversionSource,transaction.ConversionRate,transaction.ConversionRateDate,allocation.ClaimType));
        }
        var issues=transactions.Select(t=>new{Transaction=t,Balance=(t.Amount??0)-allocations.Where(a=>a.TransactionId==t.Id).Sum(a=>a.Amount)})
            .Where(x=>x.Balance!=0).Select(x=>new AnnualIssue(x.Transaction.DocumentId,$"Unallocated receipt balance: {x.Transaction.Merchant}",x.Balance)).ToList();
        var undated=await db.Documents.AsNoTracking().Where(d=>d.OwnerId==owner&&d.Purpose=="Receipt"&&!db.Transactions.Any(t=>t.DocumentId==d.Id&&(t.Date!=null||t.Status=="Dismissed"))).Select(d=>new AnnualIssue(d.Id,"Receipt has no reviewed date; its financial year is unknown.",null)).Take(2001).ToListAsync(ct);
        issues.AddRange(undated);
        var approvedTotal=rows.Where(r=>r.Status=="Approved").Sum(r=>r.DeductibleAmount??0);
        // Each myTax label omits cents once; the overall entry figure is the sum of the label entries.
        var sections=TaxCalculator.Sections(rows,ruleSet);
        return new(year,[..rows.OrderBy(r=>r.Date).ThenBy(r=>r.AllocationId)],[..issues],approvedTotal,rows.Where(r=>r.Status=="Candidate").Sum(r=>r.DeductibleAmount??0),rows.Where(r=>r.Status=="Excluded").Sum(r=>r.ExpenseAmount),rows.Where(r=>r.Status is not("Approved" or "Candidate" or "Excluded")).Sum(r=>r.ExpenseAmount),sections.Sum(x=>x.EntryAmount),sections);
    }
}
