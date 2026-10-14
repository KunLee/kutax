using System.Security.Claims;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KuTax.Api.Features.Receipts;

public static class ReceiptEndpoints
{
    public static void MapReceipts(this IEndpointRouteBuilder endpoints)
    {
        var batches = endpoints.MapGroup("/api/import-batches").RequireAuthorization();
        batches.MapPost("/", async (CreateBatchRequest request, ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            if (request.SourceKind is not ("Files" or "Folder" or "Camera") || request.ExpectedFiles is < 1 or > 2000 || request.Purpose is not ("Receipt" or "Supporting"))
                return Results.Problem("Choose 1–2,000 files per import.", statusCode: 400);
            var batch = new ImportBatch { OwnerId = Owner(principal), SourceKind = request.SourceKind, ExpectedFiles = request.ExpectedFiles, Purpose = request.Purpose };
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/import-batches/{batch.Id}", batch);
        });
        batches.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(batch => batch.Id == id && batch.OwnerId == Owner(principal), ct);
            if (batch is null) return Results.NotFound();
            var items = await db.ImportItems.AsNoTracking().Where(item => item.BatchId == id).ToListAsync(ct);
            return Results.Ok(new { batch.Id, batch.SourceKind, batch.ExpectedFiles, batch.Status, batch.CreatedAt,
                stored = items.Count(item => item.Status == "Stored"), duplicates = items.Count(item => item.Status == "Duplicate"),
                failed = items.Count(item => item.Status is "Failed" or "Unsupported" or "Cancelled"), items });
        });
        batches.MapPost("/{id:guid}/complete", async (Guid id, ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            await using var write=await db.Database.BeginTransactionAsync(ct);await LockBatch(db,id,ct);
            var batch = await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id && batch.OwnerId == Owner(principal), ct);
            if (batch is null) return Results.NotFound();
            batch.Status = "Complete"; batch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await write.CommitAsync(ct);
            return Results.NoContent();
        });
        batches.MapPut("/{id:guid}/items/{clientId:guid}", UploadAsync);
        batches.MapPost("/{id:guid}/items/{clientId:guid}/skip", async (Guid id, Guid clientId, SkipItemRequest request,
            ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            await using var write=await db.Database.BeginTransactionAsync(ct);await LockBatch(db,id,ct);
            var batch=await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id && batch.OwnerId == Owner(principal), ct);if(batch is null)return Results.NotFound();
            if (request.Status is not ("Unsupported" or "Cancelled")) return Results.BadRequest();
            string path;
            try { path = UploadValidation.NormalizePath(request.RelativePath); }
            catch (InvalidDataException ex) { return Results.Problem(ex.Message, statusCode: 400); }
            var item = await db.ImportItems.FindAsync([id, clientId], ct);
            if(item is null&&(batch.Status!="Open"||await db.ImportItems.CountAsync(i=>i.BatchId==id,ct)>=batch.ExpectedFiles))return Results.Conflict();
            if (item is null) { item = new ImportItem { BatchId = id, ClientId = clientId }; db.ImportItems.Add(item); }
            if (item.DocumentId is not null) return Results.Conflict();
            item.RelativePath = path; item.Status = request.Status;
            await db.SaveChangesAsync(ct);
            await write.CommitAsync(ct);
            return Results.Ok(item);
        });

        var documents = endpoints.MapGroup("/api/documents").RequireAuthorization();
        documents.MapGet("/", async (ClaimsPrincipal principal, KuTaxDbContext db, int? page,string? search,string? state,string? kind,string? sort,int? year,string? month,string? merchant,string? category, CancellationToken ct) =>
        {
            if (kind is not (null or "any")) return Results.BadRequest();
            if (page is < 1 or > 100000||search?.Length>100||state is not (null or "review" or "skipped")||sort is not (null or "added" or "date" or "date-asc" or "merchant" or "amount")||year is < 2000 or > 2100) return Results.BadRequest();
            // Narrowing from a report: one month (2026-03), one merchant (exact name, any case) or one kind of expense.
            DateOnly? monthStart=null;
            if(month is not null){if(!DateOnly.TryParseExact(month+"-01","yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var m))return Results.BadRequest();monthStart=m;}
            if(merchant?.Length>200||category is {Length:>40}||category is not null&&!category.All(char.IsAsciiLetter))return Results.BadRequest();
            var owner=Owner(principal);
            // Review state combines the document lifecycle with its (at most one) human-reviewed transaction.
            var rows = db.Documents.AsNoTracking().Where(document => document.OwnerId == owner && (kind == "any" || document.Purpose == "Receipt")).Select(d => new
            {
                Document = d,
                Transaction = db.Transactions.Where(t => t.DocumentId == d.Id).Select(t => new { t.Id, t.Merchant, t.Date, t.Amount, t.OriginalAmount, t.Status, t.SkipReason }).FirstOrDefault(),
                // A queued or running job means KuTax is (re)reading the receipt right now, even if a draft already exists.
                Reading = db.ProcessingJobs.Any(j => j.DocumentId == d.Id && (j.Status == "Queued" || j.Status == "Processing")),
                AiRead = db.ExtractionAttempts.Any(a => a.DocumentId == d.Id && a.Provider != "Manual"),
            }).Select(row => new
            {
                row.Document, row.Transaction, row.AiRead,
                ReviewState = row.Document.Status == "Quarantined" ? "Blocked"
                    : row.Reading ? "Processing"
                    : row.Transaction != null && row.Transaction.Status == "Confirmed" ? "Confirmed"
                    : row.Transaction != null && row.Transaction.Status == "Dismissed" ? "NotClaimed"
                    : row.Transaction != null || row.Document.Status == "NeedsReview" || row.Document.Status == "ProcessingFailed" ? "NeedsReview"
                    : "Processing",
            });
            var counts = await rows.GroupBy(row => row.ReviewState).Select(group => new { State = group.Key, Count = group.Count() }).ToListAsync(ct);
            int Count(string name) => counts.SingleOrDefault(c => c.State == name)?.Count ?? 0;
            var query = rows;
            // Search finds a receipt by merchant or file name, by an amount ("71.9" also matches the foreign-currency
            // amount on the receipt) or by its date (2026-07-03 or 3/7/2026).
            if(!string.IsNullOrWhiteSpace(search))
            {
                var term=search.Trim();var upper=term.ToUpperInvariant();
                decimal? amount=decimal.TryParse(term.TrimStart('$','A','U','D',' '),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var a)?a:null;
                DateOnly? date=DateOnly.TryParseExact(term,["yyyy-MM-dd","d/M/yyyy","dd/MM/yyyy"],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var d)?d:null;
                query=query.Where(row=>row.Document.OriginalFileName.ToUpper().Contains(upper)
                    ||row.Transaction!=null&&(row.Transaction.Merchant!=null&&row.Transaction.Merchant.ToUpper().Contains(upper)
                        ||amount!=null&&(row.Transaction.Amount==amount||row.Transaction.OriginalAmount==amount)
                        ||date!=null&&row.Transaction.Date==date));
            }
            if(year is int start){var from=new DateOnly(start,7,1);var to=from.AddYears(1);query=query.Where(row=>row.Transaction!=null&&row.Transaction.Date>=from&&row.Transaction.Date<to);}
            if(monthStart is DateOnly first){var next=first.AddMonths(1);query=query.Where(row=>row.Transaction!=null&&row.Transaction.Date>=first&&row.Transaction.Date<next);}
            if(!string.IsNullOrWhiteSpace(merchant)){var name=merchant.Trim().ToUpperInvariant();query=query.Where(row=>row.Transaction!=null&&row.Transaction.Merchant!=null&&row.Transaction.Merchant.Trim().ToUpper()==name);}
            if(category is not null)query=query.Where(row=>row.Transaction!=null&&db.Allocations.Any(a=>a.OwnerId==owner&&a.IsActive&&a.TransactionId==row.Transaction.Id&&a.Category==category));
            if(state=="review")query=query.Where(row=>row.ReviewState=="NeedsReview");
            if(state=="skipped")query=query.Where(row=>row.ReviewState=="NotClaimed");
            var total = await query.CountAsync(ct);
            // In the review queue, receipts AI has already filled in come first: they only need checking.
            // Receipts without the sorted value (no date or merchant yet) go last, then newest added.
            var ordered = sort switch
            {
                "date" => query.OrderBy(row => row.Transaction == null || row.Transaction.Date == null).ThenByDescending(row => row.Transaction!.Date).ThenByDescending(row => row.Document.CreatedAt),
                "date-asc" => query.OrderBy(row => row.Transaction == null || row.Transaction.Date == null).ThenBy(row => row.Transaction!.Date).ThenByDescending(row => row.Document.CreatedAt),
                "merchant" => query.OrderBy(row => row.Transaction == null || row.Transaction.Merchant == null).ThenBy(row => row.Transaction!.Merchant!.ToUpper()).ThenByDescending(row => row.Document.CreatedAt),
                "amount" => query.OrderBy(row => row.Transaction == null || row.Transaction.Amount == null).ThenByDescending(row => row.Transaction!.Amount).ThenByDescending(row => row.Document.CreatedAt),
                _ => state == "review" ? query.OrderByDescending(row => row.AiRead).ThenByDescending(row => row.Document.CreatedAt) : query.OrderByDescending(row => row.Document.CreatedAt),
            };
            var items = await ordered.ThenBy(row => row.Document.Id)
                .Skip(((page ?? 1) - 1) * 50).Take(50).Select(row => new DocumentListItem(row.Document.Id,
                    row.Document.OriginalFileName, row.Document.RelativePath, row.Document.ContentType, row.Document.Size, row.Document.Status,
                    row.Document.CreatedAt, row.Document.Version, row.Document.ImportBatchId, row.ReviewState,
                    row.Transaction == null ? null : row.Transaction.Merchant, row.Transaction == null ? null : row.Transaction.Date,
                    row.Transaction == null ? null : row.Transaction.Amount, row.AiRead, row.Transaction == null ? null : row.Transaction.SkipReason)).ToListAsync(ct);
            return Results.Ok(new { items, total, page = page ?? 1, pageSize = 50,
                counts = new { total = counts.Sum(c => c.Count), needsReview = Count("NeedsReview"), processing = Count("Processing"), confirmed = Count("Confirmed"), notClaimed = Count("NotClaimed"), blocked = Count("Blocked") } });
        });
        // Removes a mistaken upload completely. Refused once the file is part of a tax pack or of any claim decision, because
        // those records must stay traceable (and the ATO expects records to be kept for five years).
        documents.MapPost("/{id:guid}/delete", async (Guid id, DeleteDocumentRequest request, ClaimsPrincipal principal, KuTaxDbContext db, IDocumentStorage storage, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (!request.Confirm) return Results.BadRequest();
            var owner = Owner(principal);
            var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == owner, ct);
            if (document is null) return Results.NotFound();
            var key = id.ToString();
            if (await db.ExportSnapshots.AnyAsync(s => s.OwnerId == owner && s.SnapshotJson.Contains(key), ct))
                return Results.Problem("This file is in a tax pack you prepared, so it has to be kept. Skip the receipt instead.", statusCode: 409);
            var transaction = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.DocumentId == id && t.OwnerId == owner, ct);
            var allocationIds = transaction is null ? [] : await db.Allocations.Where(a => a.TransactionId == transaction.Id && a.OwnerId == owner).Select(a => a.Id).ToListAsync(ct);
            if (await db.TaxClaims.AnyAsync(c => c.OwnerId == owner && allocationIds.Contains(c.AllocationId), ct))
                return Results.Problem("A claim was approved from this receipt, so its record has to be kept. Skip the receipt instead.", statusCode: 409);
            if (await db.EvidenceLinks.AnyAsync(e => e.OwnerId == owner && e.DocumentId == id && !allocationIds.Contains(e.AllocationId), ct))
                return Results.Problem("This file supports another receipt's claim. Remove it from that claim's evidence first.", statusCode: 409);
            if (await db.ProcessingJobs.AnyAsync(j => j.DocumentId == id && j.Status == "Processing", ct))
                return Results.Problem("KuTax is reading this file right now. Try again in a moment.", statusCode: 409);
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await db.EvidenceLinks.Where(e => e.OwnerId == owner && allocationIds.Contains(e.AllocationId)).ExecuteDeleteAsync(ct);
                await db.Allocations.Where(a => a.OwnerId == owner && allocationIds.Contains(a.Id)).ExecuteDeleteAsync(ct);
                await db.Transactions.Where(t => t.DocumentId == id && t.OwnerId == owner).ExecuteDeleteAsync(ct);
                await db.ExtractionAttempts.Where(e => e.DocumentId == id).ExecuteDeleteAsync(ct);
                await db.ProcessingJobs.Where(j => j.DocumentId == id).ExecuteDeleteAsync(ct);
                await db.ImportItems.Where(i => i.DocumentId == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.DocumentId, (Guid?)null).SetProperty(i => i.Status, "Deleted"), ct);
                db.Documents.Remove(document);
                db.AuditEvents.Add(new() { OwnerId = owner, ActorId=Actor(principal), ResourceId = id, Action = "DocumentDeleted",
                    DetailsJson = JsonSerializer.Serialize(new { document.OriginalFileName, document.Sha256, document.Size, merchant = transaction?.Merchant, date = transaction?.Date, amount = transaction?.Amount }, ReceiptCandidate.Json) });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            // The stored original goes last: if this fails the record is already gone and the object is just unreferenced.
            try { await storage.DeleteAsync(document.ObjectKey, CancellationToken.None); }
            catch (Exception error) { logs.CreateLogger("Documents").LogWarning("Stored original for a deleted document couldn't be removed ({ExceptionType}).", error.GetType().Name); }
            return Results.NoContent();
        });
        documents.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == id && document.OwnerId == Owner(principal), ct);
            return document is null ? Results.NotFound() : Results.Ok(Response(document));
        });
        documents.MapGet("/{id:guid}/content", async (Guid id, HttpContext context, KuTaxDbContext db, IDocumentStorage storage, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == id && document.OwnerId == Owner(context.User), ct);
            if (document is null) { context.Response.StatusCode = 404; return; }
            if(document.ScanStatus!="Clean"||document.Status=="Quarantined"){context.Response.StatusCode=409;return;}
            await using var stored = await storage.OpenReadAsync(document.ObjectKey, ct);
            context.Response.ContentType = document.ContentType;
            context.Response.ContentLength = stored.Length;
            // Content is always served through owner authentication; no public URLs or active HTML.
            context.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
            // Browser PDF viewers refuse to run under the sandbox policy, so "download=1" hands the file to the device's own viewer.
            var disposition = context.Request.Query["download"] == "1" ? "attachment" : "inline";
            context.Response.Headers["Content-Disposition"] = $"{disposition}; filename*=UTF-8''{Uri.EscapeDataString(document.OriginalFileName)}";
            await stored.Content.CopyToAsync(context.Response.Body, ct);
        });
    }

    private static async Task<IResult> UploadAsync(Guid id, Guid clientId, HttpContext context, KuTaxDbContext db,
        IDocumentStorage storage, CancellationToken ct)
    {
        // Serialize this bounded batch's writes, including count checks and client-id retries.
        // EF savepoints keep duplicate recovery usable inside this transaction.
        await using var write=await db.Database.BeginTransactionAsync(ct);await LockBatch(db,id,ct);
        var result=await UploadCore(id,clientId,context,db,storage,ct);await write.CommitAsync(ct);return result;
    }
    private static Task<int> LockBatch(KuTaxDbContext db,Guid id,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()},0))",ct);
    private static async Task<IResult> UploadCore(Guid id, Guid clientId, HttpContext context, KuTaxDbContext db,
        IDocumentStorage storage, CancellationToken ct)
    {
        var ownerId = Owner(context.User);
        var batch = await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id && batch.OwnerId == ownerId, ct);
        if (batch is null) return Results.NotFound();
        var item = await db.ImportItems.FindAsync([id, clientId], ct);
        if (item?.DocumentId is not null) return Results.Ok(item);
        if (item is null && (batch.Status != "Open" || await db.ImportItems.CountAsync(item => item.BatchId == id, ct) >= batch.ExpectedFiles))
            return Results.Problem("This import is complete or has reached its file count.", statusCode: 409);
        string filename, path;
        try
        {
            filename = Uri.UnescapeDataString(context.Request.Headers["X-File-Name"].ToString());
            path = UploadValidation.NormalizePath(Uri.UnescapeDataString(context.Request.Headers["X-Relative-Path"].ToString()));
        }
        catch (Exception ex) when (ex is InvalidDataException or UriFormatException)
        { return Results.Problem("Choose a valid file name and relative path.", statusCode: 400); }
        if (context.Request.ContentLength > UploadValidation.MaxBytes) return Results.Problem("Each receipt must be 20 MB or smaller.", statusCode: 413);
        item ??= new ImportItem { BatchId = id, ClientId = clientId };
        if (db.Entry(item).State == EntityState.Detached) db.ImportItems.Add(item);
        item.RelativePath = path;
        var tempPath = Path.Combine(Path.GetTempPath(), $"kutax-upload-{Guid.NewGuid():N}");
        await using var scratch = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        (string Hash, string ContentType, long Size) metadata;
        try { metadata = await UploadValidation.CopyAndValidateAsync(context.Request.Body, scratch, filename, ct); }
        catch (InvalidDataException ex)
        {
            item.Status = "Failed"; item.Message = ex.Message;
            await db.SaveChangesAsync(ct);
            return Results.Problem(ex.Message, statusCode: 400);
        }
        var existing = await db.Documents.SingleOrDefaultAsync(document => document.OwnerId == ownerId && document.Sha256 == metadata.Hash, ct);
        if (existing is not null)
        {
            item.DocumentId = existing.Id; item.Status = "Duplicate"; item.Message = null;
            await db.SaveChangesAsync(ct); return Results.Ok(item);
        }
        var document = new ReceiptDocument
        {
            OwnerId = ownerId, ImportBatchId = id, ObjectKey = $"documents/{Guid.NewGuid():N}", OriginalFileName = filename,
            RelativePath = path, Sha256 = metadata.Hash, ContentType = metadata.ContentType, Size = metadata.Size, Purpose = batch.Purpose
        };
        try { await storage.PutAsync(document.ObjectKey, scratch, document.ContentType, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try{await storage.DeleteAsync(document.ObjectKey,CancellationToken.None);}catch{/* Operator reconciliation covers failed cleanup. */}
            item.Status = "Failed"; item.Message = "Private storage is unavailable. Retry this file.";
            await db.SaveChangesAsync(ct);
            return Results.Problem(item.Message, statusCode: 503);
        }
        db.Documents.Add(document);
        db.ProcessingJobs.Add(new ProcessingJob { DocumentId=document.Id,OwnerId=ownerId });
        item.DocumentId = document.Id; item.Status = "Stored"; item.Message = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            await storage.DeleteAsync(document.ObjectKey, CancellationToken.None);
            db.ChangeTracker.Clear();
            var replay = await db.ImportItems.AsNoTracking().SingleOrDefaultAsync(item => item.BatchId == id && item.ClientId == clientId, ct);
            if (replay is not null) return Results.Ok(replay);
            var duplicate = await db.Documents.SingleOrDefaultAsync(doc => doc.OwnerId == ownerId && doc.Sha256 == metadata.Hash, ct);
            if (duplicate is null) throw;
            item = new ImportItem { BatchId = id, ClientId = clientId, RelativePath = path, Status = "Duplicate", DocumentId = duplicate.Id };
            db.ImportItems.Add(item); await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(document.ObjectKey, CancellationToken.None);
            throw;
        }
        return Results.Ok(item);
    }

    private static string Owner(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Owner(principal);
    private static string Actor(ClaimsPrincipal principal)=>KuTax.Api.Features.Sharing.Taxpayer.Actor(principal);
    private static DocumentResponse Response(ReceiptDocument document) => new(document.Id, document.OriginalFileName,
        document.RelativePath, document.ContentType, document.Size, document.Status, document.CreatedAt, document.Version, document.ImportBatchId);
}

public sealed record CreateBatchRequest(string SourceKind, int ExpectedFiles, string Purpose = "Receipt");
public sealed record SkipItemRequest(string RelativePath, string Status);
public sealed record DocumentResponse(Guid Id, string OriginalFileName, string RelativePath, string ContentType,
    long Size, string Status, DateTimeOffset CreatedAt, Guid Version, Guid ImportBatchId);
public sealed record DeleteDocumentRequest(bool Confirm);
public sealed record DocumentListItem(Guid Id, string OriginalFileName, string RelativePath, string ContentType,
    long Size, string Status, DateTimeOffset CreatedAt, Guid Version, Guid ImportBatchId, string ReviewState,
    string? Merchant, DateOnly? Date, decimal? Amount, bool AiRead, string? SkipReason = null);
