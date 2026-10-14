using System.Security.Claims;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Sharing;

/// <summary>
/// Whose records a request works on (the owner) and who is doing it (the actor). They differ only when someone acts
/// for a person who shared their tax records with them and accepted; records are always read and written as the owner,
/// and every change is recorded against the actor.
/// </summary>
public static class Taxpayer
{
    public const string Header = "X-KuTax-Act-As";
    private const string OwnerClaim = "kutax:owner";

    public static string Owner(ClaimsPrincipal principal) => principal.FindFirstValue(OwnerClaim) ?? Actor(principal);
    public static string Actor(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Honours the act-as header only for an active, accepted delegation from an enabled owner to this user. Anything
    /// else is refused before an endpoint runs, so no endpoint can read another person's records by mistake.
    /// Account, sign-in and sharing endpoints always work on the signed-in person's own account.
    /// </summary>
    public static async Task ResolveAsync(HttpContext context, Func<Task> next)
    {
        var requested = context.Request.Headers[Header].ToString();
        var path = context.Request.Path;
        if (string.IsNullOrEmpty(requested) || context.User.Identity?.IsAuthenticated != true || !path.StartsWithSegments("/api")
            || path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/api/accounts") || path.StartsWithSegments("/api/sharing")
            || path.StartsWithSegments("/api/users") || requested == Actor(context.User))
        {
            await next();
            return;
        }
        var actor = Actor(context.User);
        var db = context.RequestServices.GetRequiredService<KuTaxDbContext>();
        var allowed = await db.Delegations.AsNoTracking().AnyAsync(d => d.OwnerId == requested && d.DelegateId == actor && d.Status == "Active", context.RequestAborted)
            && await context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>().Users.AnyAsync(u => u.Id == requested && u.IsEnabled, context.RequestAborted);
        if (!allowed)
        {
            await Results.Problem("You no longer have access to these tax records.", statusCode: 403,
                extensions: new Dictionary<string, object?> { ["code"] = "DelegationEnded" }).ExecuteAsync(context);
            return;
        }
        context.User.AddIdentity(new ClaimsIdentity([new Claim(OwnerClaim, requested)]));
        await next();
    }
}
