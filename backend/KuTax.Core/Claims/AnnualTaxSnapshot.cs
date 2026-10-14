namespace KuTax.Core.Claims;

public sealed record EvidenceReference(Guid Id,string Name,string Sha256,long Size,string ObjectKey,string ContentType);
public sealed record ClaimRow(Guid AllocationId,Guid AllocationVersion,Guid TransactionId,Guid TransactionVersion,Guid DocumentId,string Merchant,DateOnly Date,decimal InvoiceAmount,string Description,string Category,decimal ExpenseAmount,decimal WorkUsePercent,decimal WorkReimbursement,string Basis,string Status,decimal? DeductibleAmount,string Reason,string? RuleVersion,Guid? ClaimId,string? ApprovedBy,DateTimeOffset? ApprovedAt,EvidenceReference[] Evidence,
    string? OriginalCurrency=null,decimal? OriginalAmount=null,string? ConversionSource=null,decimal? ConversionRate=null,DateOnly? ConversionRateDate=null,string? ClaimType=null);
public sealed record AnnualIssue(Guid DocumentId,string Description,decimal? Amount);
public sealed record YearAssessment(int FinancialYearStart,ClaimRow[] Rows,AnnualIssue[] Issues,decimal ApprovedTotal,decimal CandidateTotal,decimal ExcludedExpenseTotal,decimal UnassessedExpenseTotal,decimal EntryAmount,MyTaxSection[]? Sections=null);
public sealed record RuleSignoff(TaxRule Rule,string ActorId,DateTimeOffset ReviewedAt,string Note);
public sealed record AnnualTaxSnapshot(Guid Id,string ModelVersion,int FinancialYearStart,string ApprovedBy,DateTimeOffset ApprovedAt,string ApprovalNote,YearAssessment Assessment,RuleSignoff[] Rules,EvidenceReference[] Evidence);

public sealed class ExportSnapshot
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public int FinancialYearStart { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
public sealed class ExportJob
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? ErrorCode { get; set; }
    public string FilesJson { get; set; } = "[]";
}
public sealed class EntryProgress
{
    public Guid SnapshotId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Section { get; set; } = "Other work-related expenses";
    public string Status { get; set; } = "NotStarted";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record ExportFile(string Kind,string FileName,string ContentType,string ObjectKey,long Size,string Sha256);
public interface IAnnualReportGenerator
{
    Task<ExportFile[]> GenerateAsync(AnnualTaxSnapshot snapshot,string snapshotSha256,CancellationToken ct);
}
