using System.Net;
using System.Net.Http.Json;
using KuTax.Api.Features.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KuTax.Api.Tests;

public sealed class AuthenticationTests
{
    private const string Password = "Synthetic-Test-481!";

    [Fact]
    public async Task Production_cookies_require_https_and_keep_the_session_httponly()
    {
        using var factory = new ApiFactory("Production");
        await factory.CreateUserAsync("secure@kutax.test", Password);
        using var insecure = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await insecure.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken)).StatusCode);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await RefreshCsrfAsync(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "secure@kutax.test", password = Password }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var session = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("KuTax.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Public_health_works_and_private_routes_reject_anonymous_users()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health/live", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts/other", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/missing", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Login_requires_csrf_and_valid_credentials()
    {
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("owner@kutax.test", Password);
        using var client = factory.CreateClient();
        var login = new { email = "owner@kutax.test", password = Password };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", login, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new
        { email = login.email, password = "Incorrect-Password-481!" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", login, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Account_isolation_year_persistence_and_logout_work_with_real_identity_cookies()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        var other = await factory.CreateUserAsync("other@kutax.test", Password);
        using var client = factory.CreateClient();
        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new
        { email = owner.Email, password = Password }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        // Refresh after the identity changes; anonymous antiforgery tokens must stop working.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{owner.Id}/financial-year", new { startYear = 2024 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{other.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/accounts/{other.Id}/financial-year", new { startYear = 2024 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{owner.Id}/financial-year", new { startYear = 10000 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var changed = await client.PutAsJsonAsync($"/api/accounts/{owner.Id}/financial-year", new { startYear = 2024 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var account = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{owner.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2024, account!.FinancialYearStart);
        Assert.Equal(new DateOnly(2025, 6, 30), account.FinancialYearTo);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Repeated_bad_passwords_lock_the_account()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("locked@kutax.test", Password);
        using var client = factory.CreateClient();
        await RefreshCsrfAsync(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new
            { email = owner.Email, password = "Incorrect-Password-481!" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new
        { email = owner.Email, password = Password }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    internal static async Task RefreshCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var token = Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
    }
}
