using Microsoft.AspNetCore.Identity;

namespace KuTax.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int SelectedFinancialYearStart { get; set; } = 2025;
    /// <summary>When the account was created. Accounts that existed before a sharing invitation can accept it in the
    /// app; newer ones accept through the emailed link, which proves they own the address.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Optional: what they do for work, which decides which work-related expenses make sense.</summary>
    public string? Occupation { get; set; }
    /// <summary>A private note administrators keep about the account (never shown to the person).</summary>
    public string? AdminNote { get; set; }
    /// <summary>When the optional profile photo (<see cref="UserAvatar"/>) last changed; null without one.</summary>
    public DateTimeOffset? AvatarUpdatedAt { get; set; }
}

/// <summary>A profile photo, already shrunk on the device (a small JPEG, PNG or WebP). Kept apart from the account so
/// listing people never loads pictures.</summary>
public sealed class UserAvatar
{
    public string UserId { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = "";
}
