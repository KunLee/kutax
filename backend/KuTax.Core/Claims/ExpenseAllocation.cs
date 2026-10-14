namespace KuTax.Core.Claims;

public sealed class ExpenseAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Unsupported";
    /// <summary>Paper-return CLAIM TYPE code where the category's label has one (D3: C/N/S/P, D4: K/I).</summary>
    public string? ClaimType { get; set; }
    public decimal Amount { get; set; }
    public decimal WorkUsePercent { get; set; }
    // Reimbursement attributable to the work portion, not reimbursement for private expenditure.
    public decimal WorkReimbursement { get; set; }
    public string Basis { get; set; } = "";
    public bool EligibilityConfirmed { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid Version { get; set; } = Guid.NewGuid();
    public bool IsActive { get; set; } = true;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedRuleVersion { get; set; }
    public Guid? ApprovedTransactionVersion { get; set; }
}
