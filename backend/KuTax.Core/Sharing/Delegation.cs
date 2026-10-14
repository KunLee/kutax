namespace KuTax.Core.Sharing;

/// <summary>
/// One person (the owner) letting another act fully on their tax records: view, download, upload, review, approve and
/// prepare tax packs. Starts as an invitation to an email address; only works once that person signs in with the same
/// email and accepts. Either side can end it at any time.
/// </summary>
public sealed class Delegation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    /// <summary>The invited address, trimmed and lower case for matching.</summary>
    public string Email { get; set; } = "";
    /// <summary>Set when the invitation is accepted.</summary>
    public string? DelegateId { get; set; }
    /// <summary>Pending, Active, Declined, Revoked or Expired.</summary>
    public string Status { get; set; } = "Pending";
    /// <summary>SHA-256 of the invitation link's secret; the secret itself is only ever in the link.</summary>
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();

    public const int DaysValid = 7;
    public bool IsOpenInvitation(DateTimeOffset now) => Status == "Pending" && ExpiresAt > now;
}
