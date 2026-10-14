using System.Security.Claims;
using KuTax.Api.Features.Accounts;
using KuTax.Api.Features.Users;
using KuTax.Core.FinancialYears;
using KuTax.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace KuTax.Api.Features.Authentication;

public static class AuthenticationEndpoints
{
    public static void MapAuthentication(this IEndpointRouteBuilder endpoints, bool development)
    {
        var auth = endpoints.MapGroup("/api/auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!development && !context.Request.IsHttps)
                return Results.Problem("HTTPS is required for sign-in.", statusCode: 400);
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false, Secure = !development, SameSite = SameSiteMode.Strict, Path = "/"
            });
            context.Response.Headers.CacheControl = "no-store";
            return Results.NoContent();
        });
        auth.MapPost("/login", async (LoginRequest request, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)
                || request.Email.Length > 254 || request.Password.Length > 1024)
                return Results.Problem("Unable to sign in with those details.", statusCode: 401);
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || !user.IsEnabled) return Results.Problem("Unable to sign in with those details.", statusCode: 401);
            var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
            return result.Succeeded ? Results.NoContent()
                : Results.Problem("Unable to sign in with those details.", statusCode: 401);
        }).RequireRateLimiting("login");
        // Self-registration creates an ordinary member with every workspace feature; administrator access stays operator-provisioned.
        auth.MapGet("/registration", (IConfiguration config) => Results.Ok(new { enabled = config.GetValue<bool>("Registration:Enabled") }));
        auth.MapPost("/register", async (RegisterRequest request, ClaimsPrincipal principal, IConfiguration config,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ILoggerFactory logs, KuTax.Infrastructure.Persistence.KuTaxDbContext db) =>
        {
            // Someone invited to help with another person's taxes can sign up with the invited address even when open
            // registration is off; they then accept the invitation themselves.
            var invited = request.InviteToken is { Length: >= 20 and <= 100 } token && request.Email is { } address
                && await KuTax.Api.Features.Sharing.SharingEndpoints.IsOpenInvitationAsync(db, token, address);
            if (!config.GetValue<bool>("Registration:Enabled") && !invited) return Results.NotFound();
            if (principal.Identity?.IsAuthenticated == true) return Results.Problem("Sign out before creating another account.", statusCode: 409);
            if (!UserEndpoints.ValidAccount(request.Email, request.DisplayName) || string.IsNullOrWhiteSpace(request.DisplayName)
                || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return Results.Problem("Enter your name, a valid email address and a password.", statusCode: 400);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Australia/Perth").DateTime);
            var user = new ApplicationUser
            {
                UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(),
                SelectedFinancialYearStart = FinancialYear.DefaultForNewAccount(today).StartYear
            };
            var result = await users.CreateAsync(user, request.Password);
            if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
                return Results.Problem("We couldn’t create an account with those details. If you already have one, sign in instead.", statusCode: 409);
            if (!result.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = result.Errors.Select(error => error.Description).ToArray() });
            await signIn.SignInAsync(user, isPersistent: false);
            logs.CreateLogger("KuTax.Registration").LogInformation("Account {UserId} self-registered.", user.Id);
            return Results.Created("/api/auth/session", AccountEndpoints.ToResponse(user, false));
        }).RequireRateLimiting("register");
        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapGet("/session", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null ? Results.Unauthorized() : Results.Ok(AccountEndpoints.ToResponse(user, principal.IsInRole(ApplicationRoles.Administrator)));
        }).RequireAuthorization();
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Email, string DisplayName, string Password, string? InviteToken = null);
