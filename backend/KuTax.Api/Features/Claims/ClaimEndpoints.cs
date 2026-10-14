using System.Security.Claims;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Claims;
public static class ClaimEndpoints
{
    public static void MapClaims(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api").RequireAuthorization();
        group.MapGet("/tax-rules",async(KuTaxDbContext db,CancellationToken ct)=>Results.Ok(new{rules=(await db.LoadRulesAsync(ct)).All.OrderBy(r=>r.FinancialYearStart).ThenBy(r=>r.Category),reviews=await db.TaxRuleReviews.AsNoTracking().ToListAsync(ct)}));
        group.MapPost("/tax-rules/{version}/review",async(string version,RuleReviewRequest request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            if(!request.Confirm||string.IsNullOrWhiteSpace(request.Note)||request.Note.Length>2000||!await db.TaxRules.AnyAsync(r=>r.Version==version,ct))return Results.BadRequest();
            var owner=Actor(principal);var existing=await db.TaxRuleReviews.SingleOrDefaultAsync(r=>r.RuleVersion==version,ct);
            if(existing is not null&&!request.Supersede)return Results.Conflict();
            if(existing is null)db.TaxRuleReviews.Add(new(){RuleVersion=version,ActorId=Actor(principal),ReviewedAt=DateTimeOffset.UtcNow,Note=request.Note.Trim()});
            else
            {
                // A newer review replaces the current sign-off; the earlier one stays in the audit history and in any frozen tax packs.
                db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action="TaxRuleReviewSuperseded",DetailsJson=JsonSerializer.Serialize(new{version,previousActor=existing.ActorId,previousReviewedAt=existing.ReviewedAt,previousNote=existing.Note},ReceiptCandidate.Json)});
                existing.ActorId=Actor(principal);existing.ReviewedAt=DateTimeOffset.UtcNow;existing.Note=request.Note.Trim();
            }
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action="TaxRuleReviewed",DetailsJson=JsonSerializer.Serialize(new{version,request.Note},ReceiptCandidate.Json)});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){return Results.Conflict();}return Results.NoContent();
        }).RequireAuthorization(p=>p.RequireRole(ApplicationRoles.Administrator));
        // Signs off several unreviewed rules at once with one note (already-reviewed versions are left as they are).
        group.MapPost("/tax-rules/review-batch",async(RuleBatchReview request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            if(!request.Confirm||string.IsNullOrWhiteSpace(request.Note)||request.Note.Length>2000||request.Versions is not {Length:>0 and <=100}
                ||await db.TaxRules.CountAsync(r=>request.Versions.Contains(r.Version),ct)!=request.Versions.Distinct().Count())return Results.BadRequest();
            var owner=Actor(principal);var existing=(await db.TaxRuleReviews.Select(r=>r.RuleVersion).ToListAsync(ct)).ToHashSet();
            var versions=request.Versions.Distinct().Where(v=>!existing.Contains(v)).ToArray();
            foreach(var version in versions)
            {
                db.TaxRuleReviews.Add(new(){RuleVersion=version,ActorId=Actor(principal),ReviewedAt=DateTimeOffset.UtcNow,Note=request.Note.Trim()});
                db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),Action="TaxRuleReviewed",DetailsJson=JsonSerializer.Serialize(new{version,request.Note},ReceiptCandidate.Json)});
            }
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){return Results.Conflict();}
            return Results.Ok(new{reviewed=versions});
        }).RequireAuthorization(p=>p.RequireRole(ApplicationRoles.Administrator));
        // Financial years the owner has records in (receipts dated in them, or tax packs), for the year picker.
        group.MapGet("/tax-years",async(ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);
            var dates=await db.Transactions.AsNoTracking().Where(t=>t.OwnerId==owner&&t.Date!=null).Select(t=>t.Date!.Value).Distinct().ToListAsync(ct);
            var packs=await db.ExportSnapshots.AsNoTracking().Where(s=>s.OwnerId==owner).Select(s=>s.FinancialYearStart).Distinct().ToListAsync(ct);
            return Results.Ok(new{years=dates.Select(d=>d.Year-(d.Month<7?1:0)).Concat(packs).Distinct().OrderDescending()});
        });
        // Opening a year's figures is when that year's rules matter: ask (without waiting) whether new ATO instructions are out.
        group.MapGet("/tax-years/{year:int}/claims",async(int year,ClaimsPrincipal principal,KuTaxDbContext db,KuTax.Infrastructure.Rules.RuleCheckRequests ruleChecks,CancellationToken ct)=>
        {
            if(year is <2000 or >2100)return Results.BadRequest();
            ruleChecks.Request(year);
            return Results.Ok(await AnnualAssessment.Load(db,Owner(principal),year,ct));
        });
        group.MapPost("/allocations/{id:guid}/approve",async(Guid id,ClaimApproval request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var allocation=await db.Allocations.SingleOrDefaultAsync(a=>a.Id==id&&a.OwnerId==owner&&a.IsActive,ct);
            if(allocation is null)return Results.NotFound();
            var transaction=await db.Transactions.SingleAsync(t=>t.Id==allocation.TransactionId&&t.OwnerId==owner,ct);
            if(!request.Confirm||allocation.Version!=request.AllocationVersion||transaction.Version!=request.TransactionVersion||transaction.Date is null)return Results.Problem("The record changed or approval was not confirmed. Reload before approving.",statusCode:409);
            var year=transaction.Date.Value.Year-(transaction.Date.Value.Month<7?1:0);
            var assessment=await AnnualAssessment.Load(db,owner,year,ct);var row=assessment.Rows.Single(r=>r.AllocationId==id);
            if(row.Status=="Approved")return Results.Ok(new{row.ClaimId});
            if(row.Status!="Candidate"||row.RuleVersion!=request.RuleVersion)return Results.Problem(row.Reason,statusCode:409);
            var claim=Approve(db,owner,Actor(principal),allocation,transaction,row,year);
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){return Results.Conflict();}return Results.Ok(new{claim.Id});
        });
        group.MapGet("/allocations/{id:guid}/evidence",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);if(!await db.Allocations.AnyAsync(a=>a.Id==id&&a.OwnerId==owner&&a.IsActive,ct))return Results.NotFound();
            return Results.Ok(await db.EvidenceLinks.AsNoTracking().Where(e=>e.AllocationId==id&&e.OwnerId==owner).ToListAsync(ct));
        });
        group.MapPut("/allocations/{id:guid}/evidence",async(Guid id,EvidenceSet request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var allocation=await db.Allocations.SingleOrDefaultAsync(a=>a.Id==id&&a.OwnerId==owner&&a.IsActive,ct);if(allocation is null)return Results.NotFound();
            if(allocation.Version!=request.Version)return Results.Conflict();
            if(request.Documents is null||request.Documents.Length>20||request.Documents.Distinct().Count()!=request.Documents.Length||request.Note?.Length>500)return Results.BadRequest();
            if(await db.Documents.CountAsync(d=>d.OwnerId==owner&&request.Documents.Contains(d.Id)&&d.ScanStatus!="Infected"&&d.Status!="Quarantined",ct)!=request.Documents.Length)return Results.NotFound();
            db.EvidenceLinks.RemoveRange(await db.EvidenceLinks.Where(e=>e.AllocationId==id&&e.OwnerId==owner).ToListAsync(ct));
            db.EvidenceLinks.AddRange(request.Documents.Select(document=>new EvidenceLink{AllocationId=id,DocumentId=document,OwnerId=owner,Note=request.Note??""}));
            allocation.Version=Guid.NewGuid();allocation.Status="Pending";allocation.ApprovedAt=null;allocation.ApprovedBy=null;allocation.ApprovedRuleVersion=null;allocation.ApprovedTransactionVersion=null;
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),ResourceId=id,Action="EvidenceChanged",DetailsJson=JsonSerializer.Serialize(request.Documents,ReceiptCandidate.Json)});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}return Results.Ok(new{allocation.Version});
        });
    }
    /// <summary>Records the owner's approval of an assessed candidate: an append-only claim, the allocation's approval state and an audit event.</summary>
    internal static TaxClaim Approve(KuTaxDbContext db,string owner,string actor,ExpenseAllocation allocation,ReceiptTransaction transaction,ClaimRow row,int year,string? via=null)
    {
        var claim=new TaxClaim{OwnerId=owner,AllocationId=allocation.Id,AllocationVersion=allocation.Version,TransactionVersion=transaction.Version,FinancialYearStart=year,RuleVersion=row.RuleVersion!,DeductibleAmount=row.DeductibleAmount!.Value,ActorId=actor,EvidenceJson=JsonSerializer.Serialize(row.Evidence.Select(e=>new{e.Id,e.Sha256}),ReceiptCandidate.Json)};
        db.TaxClaims.Add(claim);allocation.Status="Approved";allocation.ApprovedBy=actor;allocation.ApprovedAt=claim.ApprovedAt;allocation.ApprovedRuleVersion=row.RuleVersion;allocation.ApprovedTransactionVersion=transaction.Version;
        // Force optimistic checks on both records; concurrent fact/evidence edits cannot retain approval.
        db.Entry(transaction).Property(t=>t.Version).IsModified=true;db.Entry(allocation).Property(a=>a.Version).IsModified=true;
        db.AuditEvents.Add(new(){OwnerId=owner,ActorId=actor,ResourceId=allocation.Id,Action="ClaimApproved",DetailsJson=JsonSerializer.Serialize(new{claim,via},ReceiptCandidate.Json)});
        return claim;
    }
    private static string Owner(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Owner(principal);
    private static string Actor(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Actor(principal);
}
public sealed record RuleReviewRequest(bool Confirm,string Note,bool Supersede=false);
public sealed record RuleBatchReview(string[] Versions,bool Confirm,string Note);
public sealed record ClaimApproval(Guid AllocationVersion,Guid TransactionVersion,string RuleVersion,bool Confirm);
public sealed record EvidenceSet(Guid Version,Guid[] Documents,string? Note);
