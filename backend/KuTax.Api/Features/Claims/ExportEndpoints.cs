using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Claims;
public static class ExportEndpoints
{
    public static void MapExports(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api").RequireAuthorization();
        group.MapGet("/tax-years/{year:int}/export-preview",async(int year,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>year is <2000 or >2100?Results.BadRequest():Results.Ok(await Preview(db,Owner(principal),year,ct)));
        group.MapPost("/tax-years/{year:int}/exports",async(int year,SnapshotApproval request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            if(year is <2000 or >2100||!request.Confirm||!request.AcknowledgeExceptions||request.Note?.Length>2000)return Results.BadRequest();
            // The note is optional; the approval itself is the confirmation, recorded with who and when.
            var note=string.IsNullOrWhiteSpace(request.Note)?"Approved in KuTax.":request.Note.Trim();
            var owner=Owner(principal);
            if(await db.ExportJobs.CountAsync(j=>j.OwnerId==owner&&(j.Status=="Queued"||j.Status=="Processing"),ct)>=2)return Results.Problem("Wait for your current packages to finish before preparing another.",statusCode:409);
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
            var preview=await Preview(db,owner,year,ct);
            if(preview.Hash!=request.PreviewHash)return Results.Problem("Records changed since validation. Refresh and review the latest figures.",statusCode:409);
            if(preview.Blockers.Length>0)return Results.Problem(string.Join(" ",preview.Blockers),statusCode:409);
            var model=new AnnualTaxSnapshot(Guid.NewGuid(),"annual-v1",year,Actor(principal),DateTimeOffset.UtcNow,note,preview.Assessment,preview.Rules,preview.Evidence);
            var json=JsonSerializer.Serialize(model,ReceiptCandidate.Json);var snapshot=new ExportSnapshot{Id=model.Id,OwnerId=owner,FinancialYearStart=year,ApprovedBy=Actor(principal),ApprovedAt=model.ApprovedAt,SnapshotJson=json,Sha256=Hash(json)};
            db.ExportSnapshots.Add(snapshot);db.ExportJobs.Add(new(){Id=model.Id,OwnerId=owner});
            // One myTax entry checklist row per label in the pack.
            foreach(var section in preview.Assessment.Sections??[])db.EntryProgress.Add(new(){SnapshotId=model.Id,OwnerId=owner,Section=section.Section});
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),ResourceId=model.Id,Action="ExportSnapshotApproved",DetailsJson=JsonSerializer.Serialize(new{snapshot.Sha256,preview.Assessment.ApprovedTotal,note},ReceiptCandidate.Json)});
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);return Results.Accepted($"/api/tax-exports/{model.Id}",new{model.Id,snapshot.Sha256});
        });
        group.MapGet("/tax-years/{year:int}/exports",async(int year,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);return Results.Ok(await db.ExportSnapshots.AsNoTracking().Where(s=>s.OwnerId==owner&&s.FinancialYearStart==year).OrderByDescending(s=>s.ApprovedAt).Take(100).Join(db.ExportJobs,s=>s.Id,j=>j.Id,(s,j)=>new{s.Id,s.ApprovedAt,s.ApprovedBy,s.Sha256,j.Status,j.ErrorCode}).ToListAsync(ct));
        });
        // Every tax pack across all years, newest first, with the figures needed for a history list.
        group.MapGet("/tax-exports",async(ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);
            var rows=await db.ExportSnapshots.AsNoTracking().Where(s=>s.OwnerId==owner).OrderByDescending(s=>s.ApprovedAt).Take(200)
                .Join(db.ExportJobs,s=>s.Id,j=>j.Id,(s,j)=>new{s.Id,s.FinancialYearStart,s.ApprovedAt,s.SnapshotJson,j.Status}).ToListAsync(ct);
            var ids=rows.Select(r=>r.Id).ToList();
            var progress=(await db.EntryProgress.AsNoTracking().Where(p=>p.OwnerId==owner&&ids.Contains(p.SnapshotId)).ToListAsync(ct))
                // A pack is only as far along as its least-progressed label.
                .GroupBy(p=>p.SnapshotId).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Status).MinBy(status=>Array.IndexOf(ProgressOrder,status))!);
            return Results.Ok(rows.Select(r=>
            {
                var model=JsonSerializer.Deserialize<AnnualTaxSnapshot>(r.SnapshotJson,ReceiptCandidate.Json)!;
                return new{r.Id,r.FinancialYearStart,r.ApprovedAt,r.Status,model.Assessment.ApprovedTotal,model.Assessment.EntryAmount,
                    approvedClaims=model.Assessment.Rows.Count(row=>row.Status=="Approved"),evidence=model.Evidence.Length,
                    entryProgress=progress.GetValueOrDefault(r.Id,"NotStarted")};
            }));
        });
        group.MapGet("/tax-exports/{id:guid}",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var snapshot=await db.ExportSnapshots.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==id&&s.OwnerId==owner,ct);if(snapshot is null)return Results.NotFound();
            var job=await db.ExportJobs.AsNoTracking().SingleAsync(j=>j.Id==id&&j.OwnerId==owner,ct);var progress=await db.EntryProgress.AsNoTracking().Where(p=>p.SnapshotId==id&&p.OwnerId==owner).ToListAsync(ct);
            var model=JsonSerializer.Deserialize<AnnualTaxSnapshot>(snapshot.SnapshotJson,ReceiptCandidate.Json)!;
            var files=JsonSerializer.Deserialize<ExportFile[]>(job.FilesJson,ReceiptCandidate.Json)??[];
            return Results.Ok(new{snapshot.Id,snapshot.Sha256,snapshot.ApprovedAt,snapshot.ApprovedBy,job.Status,job.ErrorCode,model,progress,files=files.Select(f=>new{f.Kind,f.FileName,f.Size,f.Sha256})});
        });
        group.MapPost("/tax-exports/{id:guid}/retry",async(Guid id,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var job=await db.ExportJobs.SingleOrDefaultAsync(j=>j.Id==id&&j.OwnerId==owner,ct);if(job is null)return Results.NotFound();if(job.Status!="Failed")return Results.Conflict();
            job.Status="Queued";job.Attempts=0;job.ErrorCode=null;job.AvailableAt=DateTimeOffset.UtcNow;job.LeaseToken=null;job.LeaseUntil=null;await db.SaveChangesAsync(ct);return Results.Accepted();
        });
        group.MapPut("/tax-exports/{id:guid}/entry-progress",async(Guid id,ProgressRequest request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var owner=Owner(principal);var rows=await db.EntryProgress.Where(p=>p.SnapshotId==id&&p.OwnerId==owner).ToListAsync(ct);
            // Packs made before multi-label support have a single row; without a section the only row is meant.
            var progress=request.Section is null?(rows.Count==1?rows[0]:null):rows.SingleOrDefault(p=>p.Section==request.Section);
            if(progress is null)return Results.NotFound();if(!ProgressOrder.Contains(request.Status))return Results.BadRequest();
            progress.Status=request.Status;progress.UpdatedAt=DateTimeOffset.UtcNow;db.AuditEvents.Add(new(){OwnerId=owner,ActorId=Actor(principal),ResourceId=id,Action="MyTaxEntryProgress",DetailsJson=JsonSerializer.Serialize(new{request.Status,progress.Section},ReceiptCandidate.Json)});await db.SaveChangesAsync(ct);return Results.NoContent();
        });
        group.MapGet("/tax-exports/{id:guid}/files/{kind}",async(Guid id,string kind,ClaimsPrincipal principal,KuTaxDbContext db,IDocumentStorage storage,HttpContext context,CancellationToken ct)=>
        {
            var owner=Owner(principal);var job=await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(j=>j.Id==id&&j.OwnerId==owner,ct);if(job is null)return Results.NotFound();if(job.Status!="Complete")return Results.Conflict();
            var file=(JsonSerializer.Deserialize<ExportFile[]>(job.FilesJson,ReceiptCandidate.Json)??[]).SingleOrDefault(f=>f.Kind==kind);if(file is null)return Results.NotFound();
            // Recheck evidence safety at download time, even for historical packages.
            var snapshot=await db.ExportSnapshots.AsNoTracking().SingleAsync(s=>s.Id==id&&s.OwnerId==owner,ct);var model=JsonSerializer.Deserialize<AnnualTaxSnapshot>(snapshot.SnapshotJson,ReceiptCandidate.Json)!;var evidenceIds=model.Evidence.Select(e=>e.Id).ToArray();
            if(await db.Documents.AnyAsync(d=>d.OwnerId==owner&&evidenceIds.Contains(d.Id)&&(d.ScanStatus!="Clean"||d.Status=="Quarantined"),ct))return Results.Problem("An evidence document has not cleared scanning. This package is unavailable; review your records and prepare a new snapshot.",statusCode:409);
            var content=await storage.OpenReadAsync(file.ObjectKey,ct);context.Response.RegisterForDisposeAsync(content);context.Response.Headers.CacheControl="no-store";context.Response.Headers["Content-Security-Policy"]="sandbox; default-src 'none'";return Results.File(content.Content,file.ContentType,file.FileName,enableRangeProcessing:false);
        });
        // Everything in one download, in the chosen language (English when a pack predates Chinese files): the summary,
        // the entry guide, the spreadsheet and the receipts ZIP. Same ownership and scan checks as single files.
        group.MapGet("/tax-exports/{id:guid}/download",async(Guid id,string? lang,ClaimsPrincipal principal,KuTaxDbContext db,IDocumentStorage storage,HttpContext context,CancellationToken ct)=>
        {
            var owner=Owner(principal);var job=await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(j=>j.Id==id&&j.OwnerId==owner,ct);if(job is null)return Results.NotFound();if(job.Status!="Complete")return Results.Conflict();
            var files=JsonSerializer.Deserialize<ExportFile[]>(job.FilesJson,ReceiptCandidate.Json)??[];
            var snapshot=await db.ExportSnapshots.AsNoTracking().SingleAsync(s=>s.Id==id&&s.OwnerId==owner,ct);var model=JsonSerializer.Deserialize<AnnualTaxSnapshot>(snapshot.SnapshotJson,ReceiptCandidate.Json)!;var evidenceIds=model.Evidence.Select(e=>e.Id).ToArray();
            if(await db.Documents.AnyAsync(d=>d.OwnerId==owner&&evidenceIds.Contains(d.Id)&&(d.ScanStatus!="Clean"||d.Status=="Quarantined"),ct))return Results.Problem("An evidence document has not cleared scanning. This package is unavailable; review your records and prepare a new snapshot.",statusCode:409);
            var chinese=lang=="zh";
            var chosen=new[]{"summary","guide","workbook"}.Select(kind=>(chinese?files.FirstOrDefault(f=>f.Kind==kind+"-zh"):null)??files.FirstOrDefault(f=>f.Kind==kind))
                .Append(files.FirstOrDefault(f=>f.Kind=="evidence")).OfType<ExportFile>().ToArray();
            var year=$"FY{model.FinancialYearStart}-{(model.FinancialYearStart+1)%100:00}";
            context.Response.Headers.CacheControl="no-store";
            // Built in a temporary file (deleted when the download closes): the response stream allows only asynchronous
            // writes, and a pack's receipts can be large.
            var temp=new FileStream(Path.GetTempFileName(),FileMode.Create,FileAccess.ReadWrite,FileShare.None,81920,FileOptions.DeleteOnClose|FileOptions.Asynchronous);
            try
            {
                using(var zip=new System.IO.Compression.ZipArchive(temp,System.IO.Compression.ZipArchiveMode.Create,leaveOpen:true))
                    foreach(var file in chosen)
                    {
                        await using var source=await storage.OpenReadAsync(file.ObjectKey,ct);
                        await using var entry=zip.CreateEntry(file.FileName,System.IO.Compression.CompressionLevel.Fastest).Open();
                        await source.Content.CopyToAsync(entry,ct);
                    }
                temp.Position=0;
            }
            catch{await temp.DisposeAsync();throw;}
            return Results.File(temp,"application/zip",chinese?$"KuTax报税资料_{year}.zip":$"KuTax_tax_pack_{year}.zip");
        });
    }
    public static async Task<ExportPreview> Preview(KuTaxDbContext db,string owner,int year,CancellationToken ct)
    {
        var assessment=await AnnualAssessment.Load(db,owner,year,ct);var reviews=await db.TaxRuleReviews.AsNoTracking().ToDictionaryAsync(r=>r.RuleVersion,ct);
        var rules=(await db.LoadRulesAsync(ct)).ForYear(year).OrderBy(r=>r.Version).Select(r=>reviews.TryGetValue(r.Version,out var review)?new RuleSignoff(r,review.ActorId,review.ReviewedAt,review.Note):new RuleSignoff(r,"",DateTimeOffset.MinValue,"Not reviewed")).ToArray();
        var from=new DateOnly(year,7,1);var to=from.AddYears(1);
        var rowIds=assessment.Rows.SelectMany(r=>r.Evidence).Select(e=>e.Id).Distinct().ToArray();var issueIds=assessment.Issues.Select(i=>i.DocumentId).ToArray();
        var documents=await db.Documents.AsNoTracking().Where(d=>d.OwnerId==owner&&(rowIds.Contains(d.Id)||issueIds.Contains(d.Id)||db.Transactions.Any(t=>t.DocumentId==d.Id&&t.Date>=from&&t.Date<to))).OrderBy(d=>d.Id).ToListAsync(ct);
        var evidence=documents.Where(d=>d.ScanStatus=="Clean"&&d.Status!="Quarantined").Select(d=>new EvidenceReference(d.Id,d.OriginalFileName,d.Sha256,d.Size,d.ObjectKey,d.ContentType)).ToArray();
        var blockers=new List<string>();if(!rules.Any())blockers.Add("There is no verified entry mapping for this financial year.");if(!assessment.Rows.Any(r=>r.Status=="Approved"))blockers.Add("Approve at least one eligible claim before preparing a package.");if(evidence.Length>2000||evidence.Sum(e=>e.Size)>AnnualReportGenerator.MaxEvidenceBytes)blockers.Add("Evidence exceeds the 2,000-document or 2 GiB package limit.");
        var warnings=new List<string>();if(documents.Any(d=>d.ScanStatus!="Clean"||d.Status=="Quarantined"))warnings.Add("Unscanned or quarantined documents are excluded from the evidence package.");if(assessment.Rows.Any(r=>r.Status!="Approved")||assessment.Issues.Length>0)warnings.Add("Unresolved and excluded records are outside approved totals and remain visible in the package.");
        var hash=Hash(JsonSerializer.Serialize(new{assessment,rules,evidence},ReceiptCandidate.Json));
        var inYear=await db.Transactions.AsNoTracking().Where(t=>t.OwnerId==owner&&t.Date>=from&&t.Date<to).GroupBy(t=>t.Status).Select(g=>new{g.Key,Count=g.Count()}).ToListAsync(ct);
        var counts=new PreviewCounts(inYear.Sum(g=>g.Count),inYear.Where(g=>g.Key=="Dismissed").Sum(g=>g.Count),
            assessment.Rows.Count(r=>r.Status=="Approved"),assessment.Rows.Count(r=>r.Status=="Unsupported"),assessment.Rows.Count(r=>r.Status is not("Approved" or "Unsupported" or "Excluded")),evidence.Length);
        return new(assessment,rules,evidence,blockers.ToArray(),warnings.ToArray(),hash,counts);
    }
    private static readonly string[] ProgressOrder=["NotStarted","Entered","Checked"];
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Owner(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Owner(principal);
    private static string Actor(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Actor(principal);
}
public sealed record ExportPreview(YearAssessment Assessment,RuleSignoff[] Rules,EvidenceReference[] Evidence,string[] Blockers,string[] Warnings,string Hash,PreviewCounts? Counts=null);
/// <summary>The year at a glance: receipts dated in it, how many were skipped, claimed, kept for advice or still open, and evidence files.</summary>
public sealed record PreviewCounts(int Receipts,int Skipped,int ApprovedClaims,int KeptForAdvice,int OtherOpen,int EvidenceFiles);
public sealed record SnapshotApproval(string PreviewHash,bool Confirm,bool AcknowledgeExceptions,string? Note=null);
public sealed record ProgressRequest(string Status,string? Section=null);
