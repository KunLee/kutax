using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Core.Sharing;
using KuTax.Infrastructure.Email;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Sharing;

/// <summary>
/// Sharing your tax records with someone who then acts fully for you. You invite an email address; they accept by
/// following the emailed link (or, if their account already existed, in the app); either of you can end it.
/// </summary>
public static class SharingEndpoints
{
    private const int MaxOpen = 20;

    public static void MapSharing(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sharing").RequireAuthorization();

        // Who you share with, who shares with you, and invitations waiting for your answer.
        group.MapGet("/", async (ClaimsPrincipal principal, KuTaxDbContext db, UserManager<ApplicationUser> users, IEmailSender email, CancellationToken ct) =>
        {
            var me = await users.GetUserAsync(principal);
            if (me is null) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var outgoing = await db.Delegations.AsNoTracking().Where(d => d.OwnerId == me.Id && (d.Status == "Pending" || d.Status == "Active"))
                .ToListAsync(ct);
            outgoing = [.. outgoing.OrderBy(d => d.CreatedAt)];
            var incoming = await db.Delegations.AsNoTracking().Where(d => d.DelegateId == me.Id && d.Status == "Active").ToListAsync(ct);
            var myEmail = Normalise(me.Email!);
            var invitations = (await db.Delegations.AsNoTracking().Where(d => d.Email == myEmail && d.Status == "Pending" && d.OwnerId != me.Id).ToListAsync(ct))
                .Where(d => d.ExpiresAt > now).ToList();
            var people = await Names(users, outgoing.Select(d => d.DelegateId).Concat(incoming.Select(d => d.OwnerId)).Concat(invitations.Select(d => d.OwnerId)), ct);
            return Results.Ok(new
            {
                emailEnabled = email.Enabled,
                outgoing = outgoing.Select(d => new { d.Id, d.Email, Status = d.Status == "Pending" && d.ExpiresAt <= now ? "Expired" : d.Status, d.CreatedAt, d.ExpiresAt, d.AcceptedAt,
                    Person = d.DelegateId is null ? null : people.GetValueOrDefault(d.DelegateId) }),
                incoming = incoming.Select(d => new { d.Id, d.OwnerId, Person = people.GetValueOrDefault(d.OwnerId), d.AcceptedAt }),
                // Accepting here needs an account that existed before the invitation; newer accounts use the emailed link.
                invitations = invitations.Select(d => new { d.Id, d.OwnerId, Person = people.GetValueOrDefault(d.OwnerId), d.ExpiresAt, CanAcceptHere = me.CreatedAt < d.CreatedAt }),
            });
        });

        // Suggestions while typing an address: people you've shared with before (either way) first, then other people
        // with an account here. Accounts are only matched once two characters are typed, and at most a few are returned.
        group.MapGet("/people", async (string? q, ClaimsPrincipal principal, KuTaxDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) =>
        {
            var me = await users.GetUserAsync(principal);
            if (me is null) return Results.Unauthorized();
            var query = (q ?? "").Trim().ToLowerInvariant();
            if (query.Length > 100) return Results.Ok(Array.Empty<object>());
            var myEmail = Normalise(me.Email!);
            var links = await db.Delegations.AsNoTracking().Where(d => d.OwnerId == me.Id || d.DelegateId == me.Id || d.Email == myEmail).ToListAsync(ct);
            var sharedNow = links.Where(d => d.OwnerId == me.Id && (d.Status == "Pending" || d.Status == "Active")).Select(d => d.Email).ToHashSet();
            var knownIds = links.SelectMany(d => new[] { d.OwnerId, d.DelegateId }).OfType<string>().Where(id => id != me.Id).ToHashSet();
            var knownEmails = links.Where(d => d.OwnerId == me.Id).Select(d => d.Email).ToHashSet();
            var known = await users.Users.AsNoTracking().Where(u => knownIds.Contains(u.Id)).ToListAsync(ct);
            var suggestions = known.Select(u => (Name: u.DisplayName, Email: Normalise(u.Email!), Known: true))
                .Concat(knownEmails.Select(e => (Name: "", Email: e, Known: true)))
                .Where(p => p.Email != myEmail && !sharedNow.Contains(p.Email) && Matches(p.Name, p.Email, query))
                .DistinctBy(p => p.Email).OrderBy(p => p.Name == "" ? p.Email : p.Name).Take(6).ToList();
            if (query.Length >= 2 && suggestions.Count < 6)
            {
                var taken = suggestions.Select(p => p.Email).Append(myEmail).Concat(sharedNow).ToList();
                var others = await users.Users.AsNoTracking()
                    .Where(u => u.IsEnabled && u.Email != null && (u.Email.ToLower().Contains(query) || u.DisplayName.ToLower().Contains(query)))
                    .OrderBy(u => u.Email).Take(12).ToListAsync(ct);
                suggestions.AddRange(others.Select(u => (Name: u.DisplayName, Email: Normalise(u.Email!), Known: false))
                    .Where(p => !taken.Contains(p.Email)).Take(6 - suggestions.Count));
            }
            return Results.Ok(suggestions.Select(p => new { name = p.Name, email = p.Email, known = p.Known }));
        });

        group.MapPost("/invite", async (InviteRequest request, ClaimsPrincipal principal, HttpContext http, KuTaxDbContext db, UserManager<ApplicationUser> users, IEmailSender email, IConfiguration config, CancellationToken ct) =>
        {
            var me = await users.GetUserAsync(principal);
            if (me is null) return Results.Unauthorized();
            if (request.Email is not { Length: <= 254 } || !new EmailAddressAttribute().IsValid(request.Email.Trim())) return Results.Problem("Enter a valid email address.", statusCode: 400);
            var address = Normalise(request.Email);
            if (address == Normalise(me.Email!)) return Results.Problem("That’s your own email address.", statusCode: 400);
            var now = DateTimeOffset.UtcNow;
            // Expiry is compared after loading: a handful of rows, and portable across databases.
            var existing = await db.Delegations.AsNoTracking().Where(d => d.OwnerId == me.Id && d.Email == address && (d.Status == "Active" || d.Status == "Pending")).ToListAsync(ct);
            if (existing.Any(d => d.Status == "Active" || d.ExpiresAt > now))
                return Results.Problem("This person already has access or an invitation. Resend it from the list if they need the link again.", statusCode: 409);
            if (await db.Delegations.CountAsync(d => d.OwnerId == me.Id && (d.Status == "Pending" || d.Status == "Active"), ct) >= MaxOpen)
                return Results.Problem($"You can share with up to {MaxOpen} people at a time.", statusCode: 409);
            // An expired invitation to the same address makes way for the new one.
            await db.Delegations.Where(d => d.OwnerId == me.Id && d.Email == address && d.Status == "Pending").ExecuteUpdateAsync(u => u.SetProperty(d => d.Status, "Expired").SetProperty(d => d.EndedAt, DateTimeOffset.UtcNow), ct);
            var delegation = new Delegation { OwnerId = me.Id, Email = address };
            var token = NewToken(delegation);
            db.Delegations.Add(delegation);
            db.AuditEvents.Add(new() { OwnerId = me.Id, ActorId = me.Id, ResourceId = delegation.Id, Action = "SharingInvited", DetailsJson = JsonSerializer.Serialize(new { delegation.Email }) });
            await db.SaveChangesAsync(ct);
            var link = Link(http, config, token);
            var sent = await email.SendAsync(Invitation(me, request.Email.Trim(), link), ct);
            return Results.Ok(new { delegation.Id, link, emailed = sent });
        });

        // A fresh link (the old one stops working), sent again if email is set up.
        group.MapPost("/{id:guid}/resend", async (Guid id, ClaimsPrincipal principal, HttpContext http, KuTaxDbContext db, UserManager<ApplicationUser> users, IEmailSender email, IConfiguration config, CancellationToken ct) =>
        {
            var me = await users.GetUserAsync(principal);
            if (me is null) return Results.Unauthorized();
            var delegation = await db.Delegations.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == me.Id && d.Status == "Pending", ct);
            if (delegation is null) return Results.NotFound();
            var token = NewToken(delegation);
            delegation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(Delegation.DaysValid);
            delegation.CreatedAt = DateTimeOffset.UtcNow;
            delegation.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            var link = Link(http, config, token);
            var sent = await email.SendAsync(Invitation(me, delegation.Email.ToLowerInvariant(), link), ct);
            return Results.Ok(new { delegation.Id, link, emailed = sent });
        });

        // Ends sharing: the owner withdraws an invitation or access, or the person helping steps away.
        group.MapPost("/{id:guid}/end", async (Guid id, ClaimsPrincipal principal, KuTaxDbContext db, CancellationToken ct) =>
        {
            var me = Taxpayer.Actor(principal);
            var delegation = await db.Delegations.SingleOrDefaultAsync(d => d.Id == id && (d.OwnerId == me || d.DelegateId == me) && (d.Status == "Pending" || d.Status == "Active"), ct);
            if (delegation is null) return Results.NotFound();
            delegation.Status = "Revoked"; delegation.EndedAt = DateTimeOffset.UtcNow; delegation.Version = Guid.NewGuid();
            db.AuditEvents.Add(new() { OwnerId = delegation.OwnerId, ActorId = me, ResourceId = delegation.Id, Action = "SharingEnded" });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Accept or decline in the app: for an invitation to your address, if your account existed before it was sent.
        group.MapPost("/{id:guid}/accept", (Guid id, ClaimsPrincipal principal, KuTaxDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) =>
            Answer(db, users, principal, d => d.Id == id, accept: true, viaLink: false, ct));
        group.MapPost("/{id:guid}/decline", (Guid id, ClaimsPrincipal principal, KuTaxDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) =>
            Answer(db, users, principal, d => d.Id == id, accept: false, viaLink: false, ct));
        // Accept through the emailed link: holding it shows the address is yours.
        group.MapPost("/accept-link", (LinkRequest request, ClaimsPrincipal principal, KuTaxDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) =>
        {
            var hash = Hash(request.Token ?? "");
            return Answer(db, users, principal, d => d.TokenHash == hash, accept: true, viaLink: true, ct);
        });

        // What an invitation link is, for the page it opens (signed in or not). Reveals nothing without the secret link.
        endpoints.MapGet("/api/invitations/{token}", async (string token, KuTaxDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) =>
        {
            if (token.Length is < 20 or > 100) return Results.NotFound();
            var hash = Hash(token);
            var delegation = await db.Delegations.AsNoTracking().SingleOrDefaultAsync(d => d.TokenHash == hash, ct);
            if (delegation is null) return Results.NotFound();
            var owner = await users.FindByIdAsync(delegation.OwnerId);
            var state = delegation.Status == "Pending" && delegation.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : delegation.Status;
            return Results.Ok(new { from = Person(owner), email = delegation.Email.ToLowerInvariant(), state });
        }).AllowAnonymous().RequireRateLimiting("register");

        // What people you shared with have done in your records, newest first.
        group.MapGet("/activity", async (ClaimsPrincipal principal, KuTaxDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) =>
        {
            var me = Taxpayer.Actor(principal);
            var events = (await db.AuditEvents.AsNoTracking().Where(e => e.OwnerId == me && e.ActorId != me)
                .Select(e => new { e.ActorId, e.Action, e.CreatedAt }).ToListAsync(ct)).OrderByDescending(e => e.CreatedAt).Take(100).ToList();
            var people = await Names(users, events.Select(e => (string?)e.ActorId), ct);
            return Results.Ok(events.Select(e => new { Person = people.GetValueOrDefault(e.ActorId), e.Action, e.CreatedAt }));
        });
    }

    private static async Task<IResult> Answer(KuTaxDbContext db, UserManager<ApplicationUser> users, ClaimsPrincipal principal, System.Linq.Expressions.Expression<Func<Delegation, bool>> which,
        bool accept, bool viaLink, CancellationToken ct)
    {
        var me = await users.GetUserAsync(principal);
        if (me is null) return Results.Unauthorized();
        var delegation = await db.Delegations.Where(which).SingleOrDefaultAsync(ct);
        if (delegation is null || delegation.OwnerId == me.Id) return Results.NotFound();
        if (!delegation.IsOpenInvitation(DateTimeOffset.UtcNow)) return Results.Problem("This invitation has expired or was already used. Ask for a new one.", statusCode: 409);
        if (delegation.Email != Normalise(me.Email!))
            return Results.Problem($"This invitation is for {delegation.Email.ToLowerInvariant()}. Sign in with that email address to accept it.", statusCode: 409);
        if (!viaLink && me.CreatedAt >= delegation.CreatedAt) return Results.Problem("Open the link in the invitation email to accept it.", statusCode: 409);
        delegation.Status = accept ? "Active" : "Declined";
        delegation.DelegateId = accept ? me.Id : null;
        if (accept) delegation.AcceptedAt = DateTimeOffset.UtcNow; else delegation.EndedAt = DateTimeOffset.UtcNow;
        delegation.TokenHash = Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))); // the link is single-use
        delegation.Version = Guid.NewGuid();
        db.AuditEvents.Add(new() { OwnerId = delegation.OwnerId, ActorId = me.Id, ResourceId = delegation.Id, Action = accept ? "SharingAccepted" : "SharingDeclined" });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Results.Problem("This invitation changed. Refresh and try again.", statusCode: 409); }
        return Results.Ok(new { delegation.Id, delegation.OwnerId, delegation.Status });
    }

    internal static string Normalise(string email) => email.Trim().ToLowerInvariant();
    private static bool Matches(string name, string email, string query) =>
        query.Length == 0 || email.Contains(query, StringComparison.Ordinal) || name.Contains(query, StringComparison.OrdinalIgnoreCase);
    /// <summary>True when the token is a live invitation to this address (used to allow signing up with it).</summary>
    public static async Task<bool> IsOpenInvitationAsync(KuTaxDbContext db, string token, string email)
    {
        var hash = Hash(token); var address = Normalise(email);
        var delegation = await db.Delegations.AsNoTracking().SingleOrDefaultAsync(d => d.TokenHash == hash && d.Email == address);
        return delegation?.IsOpenInvitation(DateTimeOffset.UtcNow) == true;
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewToken(Delegation delegation)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        delegation.TokenHash = Hash(token);
        return token;
    }
    private static string Link(HttpContext http, IConfiguration config, string token)
    {
        var origin = config["PublicOrigin"] is { Length: > 0 } configured ? configured.TrimEnd('/') : $"{http.Request.Scheme}://{http.Request.Host}";
        return $"{origin}/invite/{token}";
    }
    private static object? Person(ApplicationUser? user) => user is null ? null : new { name = user.DisplayName, email = user.Email };
    private static async Task<Dictionary<string, object?>> Names(UserManager<ApplicationUser> users, IEnumerable<string?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(id => id is not null).Distinct().ToList();
        return (await users.Users.AsNoTracking().Where(u => wanted.Contains(u.Id)).ToListAsync(ct)).ToDictionary(u => u.Id, u => Person(u));
    }

    /// <summary>The invitation email, in English and Chinese.</summary>
    private static EmailMessage Invitation(ApplicationUser from, string to, string link)
    {
        var name = string.IsNullOrWhiteSpace(from.DisplayName) ? from.Email! : from.DisplayName;
        var who = WebUtility.HtmlEncode(name); var url = WebUtility.HtmlEncode(link);
        var text = $"{name} ({from.Email}) has invited you to help with their tax records in KuTax.\n\nOpen this link to accept (valid for {Delegation.DaysValid} days):\n{link}\n\n" +
                   $"{name}（{from.Email}）邀请你在 KuTax 中代为处理他们的报税资料。\n\n打开以下链接接受邀请（{Delegation.DaysValid} 天内有效）：\n{link}\n\nIf you weren't expecting this, ignore this email. / 如果不是你预期的邮件，请忽略。";
        var html = $"<p><strong>{who}</strong> ({WebUtility.HtmlEncode(from.Email)}) has invited you to help with their tax records in KuTax.</p>" +
                   $"<p><strong>{who}</strong> 邀请你在 KuTax 中代为处理他们的报税资料。</p>" +
                   $"<p><a href=\"{url}\" style=\"background:#ff8a5c;border:2px solid #0b3157;border-radius:10px;color:#0b3157;display:inline-block;font-weight:700;padding:10px 18px;text-decoration:none\">Accept / 接受邀请</a></p>" +
                   $"<p style=\"color:#555\">Valid for {Delegation.DaysValid} days. If you weren't expecting this, ignore this email.<br>{Delegation.DaysValid} 天内有效。如果不是你预期的邮件，请忽略。</p>";
        return new EmailMessage(to, $"{name} invited you to KuTax · {name} 邀请你使用 KuTax", text, html);
    }
}

public sealed record InviteRequest(string? Email);
public sealed record LinkRequest(string? Token);
