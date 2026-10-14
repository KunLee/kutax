using System.Net;
using System.Net.Http.Json;
using KuTax.Api.Features.Accounts;
using KuTax.Api.Features.Users;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;

public sealed class UserManagementTests
{
    private const string Password = "Synthetic-Current-481!";
    private const string TemporaryPassword = "Synthetic-Temporary-692!";
    private const string NewPassword = "Synthetic-New-Password-725!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_administrators_can_manage_users_and_all_writes_require_csrf()
    {
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("member@kutax.test", Password);
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users", Ct)).StatusCode);
        using var member = await Login(factory, "member@kutax.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/users", Create("unauthorised@kutax.test"), Ct)).StatusCode);
        using var admin = await Login(factory, "admin@kutax.test");
        admin.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", Create("missing-csrf@kutax.test"), Ct)).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<AccountResponse>("/api/auth/session", Ct))!.IsAdministrator);
    }

    [Fact]
    public async Task Created_accounts_must_change_password_before_using_private_routes()
    {
        using var factory = new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        using var admin = await Login(factory, "admin@kutax.test");
        var created = await admin.PostAsJsonAsync("/api/users", Create("new@kutax.test"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var managed = await created.Content.ReadFromJsonAsync<ManagedUserResponse>(Ct);
        Assert.True(managed!.IsEnabled);
        Assert.True(managed.MustChangePassword);
        Assert.False(managed.IsAdministrator);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", Create("new@kutax.test"), Ct)).StatusCode);
        using var member = await Login(factory, "new@kutax.test", TemporaryPassword);
        var account = await member.GetFromJsonAsync<AccountResponse>("/api/auth/session", Ct);
        Assert.True(account!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/accounts/{managed.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync("/api/accounts/me/password", new { currentPassword = TemporaryPassword, newPassword = "weak" }, Ct)).StatusCode);
        Assert.True((await member.GetFromJsonAsync<AccountResponse>("/api/auth/session", Ct))!.MustChangePassword);
        var changed = await member.PostAsJsonAsync("/api/accounts/me/password", new { currentPassword = TemporaryPassword, newPassword = NewPassword }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.MustChangePassword);
        await AuthenticationTests.RefreshCsrfAsync(member);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/accounts/{managed.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Optional_details_and_a_small_photo_can_be_added_and_removed()
    {
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("member@kutax.test", Password);
        await factory.CreateUserAsync("other@kutax.test", Password);
        using var member = await Login(factory, "member@kutax.test");
        using var other = await Login(factory, "other@kutax.test");

        var saved = await member.PutAsJsonAsync("/api/accounts/me/profile", new { displayName = "Synthetic member", phoneNumber = " +61 412 345 678 ", occupation = " Nurse " }, Ct);
        var account = (await saved.Content.ReadFromJsonAsync<AccountResponse>(Ct))!;
        Assert.Equal(("+61 412 345 678", "Nurse", (string?)null), (account.PhoneNumber, account.Occupation, account.AvatarUrl));
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync("/api/accounts/me/profile", new { displayName = "x", phoneNumber = "call me" }, Ct)).StatusCode);
        var cleared = await (await member.PutAsJsonAsync("/api/accounts/me/profile", new { displayName = "Synthetic member", phoneNumber = "" }, Ct)).Content.ReadFromJsonAsync<AccountResponse>(Ct);
        Assert.Null(cleared!.PhoneNumber);

        // Only real pictures, and only small ones (the app shrinks them first).
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync("/api/accounts/me/avatar", new { image = Convert.ToBase64String("<svg/>"u8.ToArray()) }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync("/api/accounts/me/avatar", new { image = Convert.ToBase64String([.. png, .. new byte[201 * 1024]]) }, Ct)).StatusCode);
        var withPhoto = await (await member.PutAsJsonAsync("/api/accounts/me/avatar", new { image = Convert.ToBase64String(png) }, Ct)).Content.ReadFromJsonAsync<AccountResponse>(Ct);
        Assert.StartsWith("/api/accounts/me/avatar?v=", withPhoto!.AvatarUrl);
        var photo = await member.GetAsync(withPhoto.AvatarUrl, Ct);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await photo.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/accounts/me/avatar", Ct)).StatusCode); // each person sees their own

        var removed = await (await member.DeleteAsync("/api/accounts/me/avatar", Ct)).Content.ReadFromJsonAsync<AccountResponse>(Ct);
        Assert.Null(removed!.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/accounts/me/avatar", Ct)).StatusCode);
    }

    [Fact]
    public async Task Profile_and_password_changes_are_owner_scoped_and_revoke_other_sessions()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateUserAsync("member@kutax.test", Password);
        using var first = await Login(factory, user.Email!);
        using var second = await Login(factory, user.Email!);
        var profile = await first.PutAsJsonAsync("/api/accounts/me/profile", new { displayName = "  Synthetic owner  ", isAdministrator = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var account = await profile.Content.ReadFromJsonAsync<AccountResponse>(Ct);
        Assert.Equal("Synthetic owner", account!.DisplayName);
        Assert.False(account.IsAdministrator);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/accounts/me/password", new { currentPassword = "wrong", newPassword = NewPassword }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/accounts/me/password", new { currentPassword = Password, newPassword = Password }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/accounts/me/password", new { currentPassword = Password, newPassword = NewPassword }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/session", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/session", Ct)).StatusCode);
    }

    [Fact]
    public async Task Disable_edit_and_reset_revoke_sessions_and_disabled_accounts_cannot_sign_in()
    {
        using var factory = new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        var user = await factory.CreateUserAsync("member@kutax.test", Password);
        using var admin = await Login(factory, "admin@kutax.test");
        using var member = await Login(factory, user.Email!);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { isEnabled = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/session", Ct)).StatusCode);
        using var denied = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(denied);
        Assert.Equal(HttpStatusCode.Unauthorized, (await denied.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password }, Ct)).StatusCode);
        // Re-enabling changes nothing else: the person keeps their own name and address.
        var enabledAccount = await (await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { email = "renamed@kutax.test", displayName = "Renamed", isEnabled = true }, Ct)).Content.ReadFromJsonAsync<ManagedUserResponse>(Ct);
        Assert.Equal("member@kutax.test", enabledAccount!.Email);
        using var enabled = await Login(factory, "member@kutax.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new { temporaryPassword = TemporaryPassword }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await enabled.GetAsync("/api/auth/session", Ct)).StatusCode);
        using var reset = await Login(factory, "member@kutax.test", TemporaryPassword);
        Assert.True((await reset.GetFromJsonAsync<AccountResponse>("/api/auth/session", Ct))!.MustChangePassword);
    }

    [Fact]
    public async Task Administrators_are_protected_and_list_search_is_bounded()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        await factory.CreateUserAsync("member@kutax.test", Password);
        using var admin = await Login(factory, user.Email!);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { isEnabled = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new { temporaryPassword = TemporaryPassword }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{user.Id}/unlock", null, Ct)).StatusCode);
        var page = await admin.GetFromJsonAsync<UserPage>("/api/users?search=MEMBER", Ct);
        Assert.Equal(1, page!.Total);
        Assert.Single(page.Items);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/users?page=0", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/users/missing/unlock", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Administrators_promote_others_and_keep_private_notes()
    {
        using var factory = new ApiFactory();
        var self = await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        var user = await factory.CreateUserAsync("member@kutax.test", Password);
        using var admin = await Login(factory, "admin@kutax.test");
        using var member = await Login(factory, user.Email!);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/users", Ct)).StatusCode);

        var noted = await (await admin.PutAsJsonAsync($"/api/users/{user.Id}/note", new { note = "  Synthetic: family member, joined for FY2025  " }, Ct)).Content.ReadFromJsonAsync<ManagedUserResponse>(Ct);
        Assert.Equal("Synthetic: family member, joined for FY2025", noted!.Note);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/users/{user.Id}/note", new { note = new string('x', 1001) }, Ct)).StatusCode);

        // Promoted: they sign in again and can manage accounts too.
        Assert.True((await (await admin.PutAsJsonAsync($"/api/users/{user.Id}/administrator", new { isAdministrator = true }, Ct)).Content.ReadFromJsonAsync<ManagedUserResponse>(Ct))!.IsAdministrator);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/session", Ct)).StatusCode);
        using var promoted = await Login(factory, user.Email!);
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/api/users", Ct)).StatusCode);

        // Never your own account; another administrator can step you down, which signs you out.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{self.Id}/administrator", new { isAdministrator = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await promoted.PutAsJsonAsync($"/api/users/{self.Id}/administrator", new { isAdministrator = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/session", Ct)).StatusCode);
    }

    [Fact]
    public async Task Administrator_unlock_restores_access_after_password_lockout()
    {
        using var factory = new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        var user = await factory.CreateUserAsync("locked@kutax.test", Password);
        using var denied = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(denied);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await denied.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "wrong" }, Ct)).StatusCode);
        using var admin = await Login(factory, "admin@kutax.test");
        var unlocked = await admin.PostAsync($"/api/users/{user.Id}/unlock", null, Ct);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.False((await unlocked.Content.ReadFromJsonAsync<ManagedUserResponse>(Ct))!.IsLockedOut);
        using var member = await Login(factory, user.Email!);
    }

    [Fact]
    public async Task Administrators_manage_other_administrators_and_only_empty_accounts_can_be_deleted()
    {
        using var factory = new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        var colleague = await factory.CreateAdministratorAsync("colleague@kutax.test", Password);
        var empty = await factory.CreateUserAsync("empty@kutax.test", Password);
        var keeper = await factory.CreateUserAsync("keeper@kutax.test", Password);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            db.ImportBatches.Add(new ImportBatch { OwnerId = keeper.Id, ExpectedFiles = 1 });
            await db.SaveChangesAsync(Ct);
        }
        using var admin = await Login(factory, "admin@kutax.test");

        // Another administrator can be disabled, re-enabled and given a temporary password.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{colleague.Id}", new { isEnabled = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{colleague.Id}", new { isEnabled = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{colleague.Id}/reset-password", new { temporaryPassword = TemporaryPassword }, Ct)).StatusCode);

        // The list says which accounts hold records; those can only be disabled, empty ones can be deleted.
        var page = await admin.GetFromJsonAsync<UserPage>("/api/users", Ct);
        Assert.True(page!.Items.Single(u => u.Id == keeper.Id).HasRecords);
        Assert.False(page.Items.Single(u => u.Id == empty.Id).HasRecords);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/users/{keeper.Id}/delete", new { confirm = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/users/{empty.Id}/delete", new { confirm = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/users/{empty.Id}/delete", new { confirm = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/users/{colleague.Id}/delete", new { confirm = true }, Ct)).StatusCode);
        var after = await admin.GetFromJsonAsync<UserPage>("/api/users", Ct);
        Assert.Equal(["admin@kutax.test", "keeper@kutax.test"], after!.Items.Select(u => u.Email).Order());
    }

    private static object Create(string email) => new { email, displayName = "Synthetic user", temporaryPassword = TemporaryPassword, isAdministrator = true };
    private static async Task<HttpClient> Login(ApiFactory factory, string email, string password = Password)
    {
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }
    private sealed record UserPage(ManagedUserResponse[] Items, int Total, int Page, int PageSize);
}
