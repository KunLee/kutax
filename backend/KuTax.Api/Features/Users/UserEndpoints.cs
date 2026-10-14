using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KuTax.Core.FinancialYears;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Users;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users").RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator));
        group.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (context.HttpContext.Request.Method is "POST" or "PUT"
                && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 }
                && result is IValueHttpResult { Value: ManagedUserResponse target })
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("KuTax.UserManagement")
                    .LogInformation("User management {Method} {Path} by {ActorId} for {TargetId}",
                        context.HttpContext.Request.Method, context.HttpContext.Request.Path,
                        context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), target.Id);
            return result;
        });
        group.MapGet("/", async (string? search, int? page, UserManager<ApplicationUser> users, KuTaxDbContext db, CancellationToken ct) =>
        {
            if (search?.Length > 100 || page is < 1 or > 100000) return Results.BadRequest();
            var query = users.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToUpperInvariant();
                query = query.Where(user => user.NormalizedEmail!.Contains(term) || user.DisplayName.ToUpper().Contains(term));
            }
            var total = await query.CountAsync(ct);
            var currentPage = page ?? 1;
            var accounts = await query.OrderBy(user => user.Email).ThenBy(user => user.Id).Skip((currentPage - 1) * 25).Take(25).ToListAsync(ct);
            var administrators = (await users.GetUsersInRoleAsync(ApplicationRoles.Administrator)).Select(user => user.Id).ToHashSet();
            var ids = accounts.Select(user => user.Id).ToList();
            var withRecords = (await db.ImportBatches.Where(b => ids.Contains(b.OwnerId)).Select(b => b.OwnerId)
                .Union(db.ExportSnapshots.Where(x => ids.Contains(x.OwnerId)).Select(x => x.OwnerId)).ToListAsync(ct)).ToHashSet();
            return Results.Ok(new { items = accounts.Select(user => ToResponse(user, administrators.Contains(user.Id), withRecords.Contains(user.Id))), total, page = currentPage, pageSize = 25 });
        });
        group.MapPost("/", async (CreateUserRequest request, UserManager<ApplicationUser> users) =>
        {
            if (!ValidAccount(request.Email, request.DisplayName) || string.IsNullOrEmpty(request.TemporaryPassword) || request.TemporaryPassword.Length > 1024)
                return Results.Problem("Enter a valid email, name (up to 100 characters) and temporary password.", statusCode: 400);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Australia/Perth").DateTime);
            var user = new ApplicationUser
            {
                UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(),
                MustChangePassword = true, SelectedFinancialYearStart = FinancialYear.DefaultForNewAccount(today).StartYear
            };
            var result = await users.CreateAsync(user, request.TemporaryPassword);
            return result.Succeeded ? Results.Created($"/api/users/{user.Id}", ToResponse(user)) : Failure(result);
        });
        // Administrators switch access on or off; a person's name and email are theirs (they change their name in their
        // own settings, and use a new account for a new address).
        group.MapPut("/{id}", async (string id, UpdateUserRequest request, ClaimsPrincipal actor, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (IsSelf(actor, user)) return OwnAccount();
            if (!request.IsEnabled && user.IsEnabled && await LastAdministrator(users, user)) return LastAdministratorProblem();
            user.IsEnabled = request.IsEnabled;
            // Save the change and session revocation in one optimistic-concurrency update.
            user.SecurityStamp = Guid.NewGuid().ToString();
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, await users.IsInRoleAsync(user, ApplicationRoles.Administrator))) : Failure(result);
        });
        group.MapPut("/{id}/note", async (string id, NoteRequest request, UserManager<ApplicationUser> users) =>
        {
            if (request.Note is { Length: > 1000 }) return Results.Problem("Keep the note under 1,000 characters.", statusCode: 400);
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            user.AdminNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, await users.IsInRoleAsync(user, ApplicationRoles.Administrator))) : Failure(result);
        });
        // Make someone an administrator, or take it away. They sign in again so the change applies straight away.
        group.MapPut("/{id}/administrator", async (string id, AdministratorRequest request, ClaimsPrincipal actor, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (IsSelf(actor, user)) return OwnAccount();
            var isAdministrator = await users.IsInRoleAsync(user, ApplicationRoles.Administrator);
            if (isAdministrator == request.IsAdministrator) return Results.Ok(ToResponse(user, isAdministrator));
            if (!request.IsAdministrator && await LastAdministrator(users, user)) return LastAdministratorProblem();
            var result = request.IsAdministrator ? await users.AddToRoleAsync(user, ApplicationRoles.Administrator)
                : await users.RemoveFromRoleAsync(user, ApplicationRoles.Administrator);
            if (!result.Succeeded) return Failure(result);
            await users.UpdateSecurityStampAsync(user);
            return Results.Ok(ToResponse(user, request.IsAdministrator));
        });
        group.MapPost("/{id}/unlock", async (string id, ClaimsPrincipal actor, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (IsSelf(actor, user)) return OwnAccount();
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, await users.IsInRoleAsync(user, ApplicationRoles.Administrator))) : Failure(result);
        });
        group.MapPost("/{id}/reset-password", async (string id, ResetPasswordRequest request, ClaimsPrincipal actor, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (IsSelf(actor, user)) return OwnAccount();
            if (string.IsNullOrEmpty(request.TemporaryPassword) || request.TemporaryPassword.Length > 1024) return Results.BadRequest();
            user.MustChangePassword = true;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, request.TemporaryPassword);
            return result.Succeeded ? Results.Ok(ToResponse(user)) : Failure(result);
        });
        group.MapPost("/{id}/delete", async (string id, DeleteUserRequest request, ClaimsPrincipal actor, UserManager<ApplicationUser> users, KuTaxDbContext db, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (!request.Confirm) return Results.BadRequest();
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (IsSelf(actor, user)) return OwnAccount();
            if (await LastAdministrator(users, user)) return LastAdministratorProblem();
            if (await db.ImportBatches.AnyAsync(b => b.OwnerId == id, ct) || await db.Transactions.AnyAsync(t => t.OwnerId == id, ct) || await db.ExportSnapshots.AnyAsync(x => x.OwnerId == id, ct))
                return Results.Problem("This account has receipts or tax packs, so it can't be deleted. Disable it instead: that keeps their records.", statusCode: 409);
            await db.PrivacyPreferences.Where(p => p.OwnerId == id).ExecuteDeleteAsync(ct);
            var result = await users.DeleteAsync(user);
            if (!result.Succeeded) return Failure(result);
            logs.CreateLogger("KuTax.UserManagement").LogInformation("User management delete by {ActorId} for {TargetId}", actor.FindFirstValue(ClaimTypes.NameIdentifier), id);
            return Results.NoContent();
        });
    }

    private static bool IsSelf(ClaimsPrincipal actor, ApplicationUser user) => actor.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id;
    /// <summary>True when this is an administrator and no other enabled administrator would be left.</summary>
    private static async Task<bool> LastAdministrator(UserManager<ApplicationUser> users, ApplicationUser user) =>
        await users.IsInRoleAsync(user, ApplicationRoles.Administrator)
        && !(await users.GetUsersInRoleAsync(ApplicationRoles.Administrator)).Any(other => other.Id != user.Id && other.IsEnabled);
    private static IResult OwnAccount() => Results.Problem("Use personal settings for your own account.", statusCode: 409);
    private static IResult LastAdministratorProblem() => Results.Problem("Keep at least one enabled administrator.", statusCode: 409);

    internal static bool ValidAccount(string? email, string? displayName) => email is { Length: <= 254 }
        && new EmailAddressAttribute().IsValid(email.Trim()) && displayName is not null && displayName.Trim().Length <= 100;
    private static IResult Failure(IdentityResult result) => result.Errors.Any(error => error.Code == "ConcurrencyFailure")
        ? Results.Problem("The account changed. Refresh and try again.", statusCode: 409)
        : Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = result.Errors.Select(error => error.Description).ToArray() });
    private static ManagedUserResponse ToResponse(ApplicationUser user, bool administrator = false, bool hasRecords = false) => new(user.Id,
        user.Email!, user.DisplayName, user.IsEnabled, administrator, user.MustChangePassword,
        user.LockoutEnd > DateTimeOffset.UtcNow, hasRecords, user.AdminNote, user.CreatedAt);
}

public sealed record CreateUserRequest(string Email, string DisplayName, string TemporaryPassword);
public sealed record UpdateUserRequest(bool IsEnabled);
public sealed record NoteRequest(string? Note);
public sealed record AdministratorRequest(bool IsAdministrator);
public sealed record ResetPasswordRequest(string TemporaryPassword);
public sealed record DeleteUserRequest(bool Confirm);
public sealed record ManagedUserResponse(string Id, string Email, string DisplayName, bool IsEnabled,
    bool IsAdministrator, bool MustChangePassword, bool IsLockedOut, bool HasRecords, string? Note = null, DateTimeOffset? CreatedAt = null);
