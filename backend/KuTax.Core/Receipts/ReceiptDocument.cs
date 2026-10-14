namespace KuTax.Core.Receipts;

public sealed class ReceiptDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public Guid ImportBatchId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Status { get; set; } = "Stored";
    public string ScanStatus { get; set; } = "Pending";
    public DateTimeOffset? ScannedAt { get; set; }
    /// <summary>"Receipt" (read and reviewed) or "Supporting" (extra evidence for a claim: scanned, never read or queued).</summary>
    public string Purpose { get; set; } = "Receipt";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public string SourceKind { get; set; } = "Files";
    /// <summary>What the batch's files are for; see <see cref="ReceiptDocument.Purpose"/>.</summary>
    public string Purpose { get; set; } = "Receipt";
    public int ExpectedFiles { get; set; }
    public string Status { get; set; } = "Open";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ImportItem
{
    public Guid BatchId { get; set; }
    public Guid ClientId { get; set; }
    public string RelativePath { get; set; } = "";
    public string Status { get; set; } = "Stored";
    public Guid? DocumentId { get; set; }
    public string? Message { get; set; }
}
