using System.Security.Claims;
using System.Text.RegularExpressions;
using KuTax.Core.FinancialYears;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Accounts;

public static partial class AccountEndpoints
{
    public static void MapAccounts(this IEndpointRouteBuilder endpoints)
    {
        var accounts = endpoints.MapGroup("/api/accounts").RequireAuthorization();
        accounts.MapGet("/{id}", async (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (id != users.GetUserId(principal)) return Results.NotFound();
            var user = await users.FindByIdAsync(id);
            return user is null ? Results.NotFound() : Results.Ok(ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)));
        });
        accounts.MapPut("/{id}/financial-year", async (string id, FinancialYearRequest request,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (id != users.GetUserId(principal)) return Results.NotFound();
            if (request.StartYear is < 1900 or > 9998)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["startYear"] = ["Choose a valid financial year."] });
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            user.SelectedFinancialYearStart = request.StartYear;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)))
                : Results.Problem("The account changed. Refresh and try again.", statusCode: 409);
        });
        accounts.MapPut("/me/profile", async (ProfileRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (request.DisplayName is null || request.DisplayName.Trim().Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["Use at most 100 characters."] });
            var phone = Optional(request.PhoneNumber);
            if (phone is not null && (phone.Length > 30 || !PhonePattern().IsMatch(phone) || phone.Count(char.IsAsciiDigit) < 6))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["phoneNumber"] = ["Enter a phone number, for example 0412 345 678."] });
            var occupation = Optional(request.Occupation);
            if (occupation is { Length: > 100 })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["occupation"] = ["Use at most 100 characters."] });
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            user.DisplayName = request.DisplayName.Trim();
            user.PhoneNumber = phone;
            user.Occupation = occupation;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)))
                : Results.Problem("The account changed. Refresh and try again.", statusCode: 409);
        });
        // The optional profile photo: the app shrinks it first, so only a small picture of a known image type is kept.
        accounts.MapGet("/me/avatar", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, KuTaxDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == users.GetUserId(principal), ct);
            if (avatar is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(avatar.Content, avatar.ContentType);
        });
        accounts.MapPut("/me/avatar", async (AvatarRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users, KuTaxDbContext db, CancellationToken ct) =>
        {
            byte[] content;
            try { content = Convert.FromBase64String(request.Image ?? ""); }
            catch (FormatException) { content = []; }
            var type = ImageType(content);
            if (type is null || content.Length > MaxAvatarBytes)
                return Results.Problem("Choose a JPEG, PNG or WebP picture.", statusCode: 400);
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var avatar = await db.Avatars.SingleOrDefaultAsync(a => a.UserId == user.Id, ct);
            if (avatar is null) db.Avatars.Add(avatar = new UserAvatar { UserId = user.Id });
            avatar.Content = content;
            avatar.ContentType = type;
            await db.SaveChangesAsync(ct);
            user.AvatarUpdatedAt = DateTimeOffset.UtcNow;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)))
                : Results.Problem("The account changed. Refresh and try again.", statusCode: 409);
        });
        accounts.MapDelete("/me/avatar", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, KuTaxDbContext db, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            await db.Avatars.Where(a => a.UserId == user.Id).ExecuteDeleteAsync(ct);
            user.AvatarUpdatedAt = null;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)))
                : Results.Problem("The account changed. Refresh and try again.", statusCode: 409);
        });
        accounts.MapPost("/me/password", async (ChangePasswordRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword)
                || request.CurrentPassword.Length > 1024 || request.NewPassword.Length > 1024
                || request.CurrentPassword == request.NewPassword)
                return Results.Problem("Enter your current password and a different new password.", statusCode: 400);
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            user.MustChangePassword = false;
            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["password"] = result.Errors.Select(error => error.Description).ToArray() });
            await signIn.RefreshSignInAsync(user);
            return Results.Ok(ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)));
        }).RequireRateLimiting("login");
    }

    public static AccountResponse ToResponse(ApplicationUser user, bool isAdministrator = false)
    {
        var year = new FinancialYear(user.SelectedFinancialYearStart);
        return new(user.Id, user.Email!, year.StartYear, year.Label, year.Start, year.End,
            user.DisplayName, isAdministrator, user.MustChangePassword, user.PhoneNumber, user.Occupation,
            user.AvatarUpdatedAt is { } changed ? $"/api/accounts/me/avatar?v={changed.ToUnixTimeMilliseconds()}" : null);
    }

    private const int MaxAvatarBytes = 200 * 1024;
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The picture's type from its first bytes, whatever the browser claimed.</summary>
    private static string? ImageType(ReadOnlySpan<byte> content) =>
        content.StartsWith((byte[])[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : content.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : content.Length > 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    [GeneratedRegex(@"^\+?[0-9 ()\-]+$")]
    private static partial Regex PhonePattern();
}

public sealed record FinancialYearRequest(int StartYear);
public sealed record ProfileRequest(string DisplayName, string? PhoneNumber = null, string? Occupation = null);
public sealed record AvatarRequest(string? Image);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record AccountResponse(string Id, string Email, int FinancialYearStart, string FinancialYearLabel,
    DateOnly FinancialYearFrom, DateOnly FinancialYearTo, string DisplayName, bool IsAdministrator, bool MustChangePassword,
    string? PhoneNumber = null, string? Occupation = null, string? AvatarUrl = null);
