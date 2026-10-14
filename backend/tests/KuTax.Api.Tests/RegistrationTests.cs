using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Api.Features.Accounts;
using Microsoft.AspNetCore.Hosting;

namespace KuTax.Api.Tests;

public sealed class RegistrationTests
{
    private const string Password = "Synthetic-Register-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static object Register(string email, string password = Password, string name = "Synthetic Member") =>
        new { email, displayName = name, password };

    [Fact]
    public async Task Registration_creates_a_signed_in_member_without_administrator_access()
    {
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("existing@kutax.test", Password);
        using var client = factory.CreateClient();
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/registration", Ct)).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", Register("no-csrf@kutax.test"), Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", Register("weak@kutax.test", "short"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", Register("not-an-email"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", Register("blank@kutax.test", name: " "), Ct)).StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", Register("EXISTING@kutax.test"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.DoesNotContain("existing@kutax.test", await duplicate.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);

        var created = await client.PostAsJsonAsync("/api/auth/register", Register("  new.member@kutax.test "), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        var session = await client.GetFromJsonAsync<AccountResponse>("/api/auth/session", Ct);
        Assert.Equal("new.member@kutax.test", session!.Email);
        Assert.Equal("Synthetic Member", session.DisplayName);
        Assert.False(session.IsAdministrator);
        Assert.False(session.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/privacy", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", Register("second@kutax.test"), Ct)).StatusCode);

        // The new password works for a fresh sign-in.
        using var fresh = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(fresh);
        Assert.Equal(HttpStatusCode.NoContent, (await fresh.PostAsJsonAsync("/api/auth/login", new { email = "new.member@kutax.test", password = Password }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Registration_can_be_disabled_and_is_rate_limited_per_client()
    {
        using (var disabled = new ApiFactory().WithWebHostBuilder(builder => builder.UseSetting("Registration:Enabled", "false")))
        {
            using var client = disabled.CreateClient();
            Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/registration", Ct)).GetProperty("enabled").GetBoolean());
            await AuthenticationTests.RefreshCsrfAsync(client);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/register", Register("closed@kutax.test"), Ct)).StatusCode);
        }
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("existing@kutax.test", Password);
        using var anonymous = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(anonymous);
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/register", Register($"weak{attempt}@kutax.test", "short"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anonymous.PostAsJsonAsync("/api/auth/register", Register("limited@kutax.test"), Ct)).StatusCode);
    }
}
