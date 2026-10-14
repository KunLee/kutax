using System.Net;
using System.Net.Http.Json;
using KuTax.Api.Features.Accounts;
using KuTax.Api.Features.Users;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace KuTax.IntegrationTests;

public sealed class UserManagementIntegrationTests
{
    [RequiresServices]
    public async Task User_lifecycle_persists_in_postgres_and_revokes_real_sessions()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var suffix = Guid.NewGuid().ToString("N");
        var administrator = new ApplicationUser { UserName = $"admin-{suffix}@kutax.test", Email = $"admin-{suffix}@kutax.test" };
        var memberEmail = $"member-{suffix}@kutax.test";
        const string initialPassword = "Synthetic-Administrator-481!";
        const string temporaryPassword = "Synthetic-Temporary-692!";
        const string chosenPassword = "Synthetic-Chosen-Password-725!";
        Assert.True((await users.CreateAsync(administrator, initialPassword)).Succeeded);
        try
        {
            if (!await roles.RoleExistsAsync(ApplicationRoles.Administrator))
                Assert.True((await roles.CreateAsync(new IdentityRole(ApplicationRoles.Administrator))).Succeeded);
            Assert.True((await users.AddToRoleAsync(administrator, ApplicationRoles.Administrator)).Succeeded);
            using var admin = CreateClient(factory);
            await Login(admin, administrator.Email!, initialPassword);
            var created = await admin.PostAsJsonAsync("/api/users", new { email = memberEmail, displayName = "Synthetic member", temporaryPassword }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var member = (await created.Content.ReadFromJsonAsync<ManagedUserResponse>(TestContext.Current.CancellationToken))!;
            using var client = CreateClient(factory);
            await Login(client, memberEmail, temporaryPassword);
            Assert.True((await client.GetFromJsonAsync<AccountResponse>("/api/auth/session", TestContext.Current.CancellationToken))!.MustChangePassword);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/accounts/{member.Id}/financial-year", new { startYear = 2024 }, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts/me/password", new { currentPassword = temporaryPassword, newPassword = chosenPassword }, TestContext.Current.CancellationToken)).StatusCode);
            await Csrf(client);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/accounts/me/profile", new { displayName = "Updated synthetic member" }, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/accounts/{member.Id}/financial-year", new { startYear = 2024 }, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{member.Id}", new { email = memberEmail, displayName = "Updated synthetic member", isEnabled = false }, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{member.Id}", new { email = memberEmail, displayName = "Updated synthetic member", isEnabled = true }, TestContext.Current.CancellationToken)).StatusCode);
            using var enabled = CreateClient(factory);
            await Login(enabled, memberEmail, chosenPassword);
            var persisted = (await enabled.GetFromJsonAsync<AccountResponse>("/api/auth/session", TestContext.Current.CancellationToken))!;
            Assert.Equal(2024, persisted.FinancialYearStart);
            Assert.Equal("Updated synthetic member", persisted.DisplayName);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{member.Id}/reset-password", new { temporaryPassword }, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await enabled.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
            using var reset = CreateClient(factory);
            await Login(reset, memberEmail, temporaryPassword);
            Assert.True((await reset.GetFromJsonAsync<AccountResponse>("/api/auth/session", TestContext.Current.CancellationToken))!.MustChangePassword);
        }
        finally
        {
            // These two unique synthetic accounts belong only to this test.
            var member = await users.FindByEmailAsync(memberEmail);
            if (member is not null) Assert.True((await users.DeleteAsync(member)).Succeeded);
            Assert.True((await users.DeleteAsync(administrator)).Succeeded);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        Environment.GetEnvironmentVariable("KUTAX_APP_URL") is { Length: > 0 } url
            ? new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(url) }
            : factory.CreateClient();

    private static async Task Login(HttpClient client, string email, string password)
    {
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken)).StatusCode);
        await Csrf(client);
    }

    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]));
    }
}
