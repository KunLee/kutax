using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;
public sealed class AiSettingsTests
{
    private const string Password = "Synthetic-Settings-481!";
    private const string SyntheticKey = "gsk_synthetic_test_key_ABCD1234";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static object Settings(bool enabled = true, string? apiKey = null, bool clearApiKey = false, string model = "synthetic/vision", string textModel = "synthetic/text") =>
        new { enabled, provider = "Groq", apiKey, clearApiKey, model, textModel, region = "Synthetic region", retentionNotice = "Synthetic retention notice" };

    private static async Task<HttpClient> Login(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }

    [Fact]
    public async Task Only_administrators_manage_ai_settings_and_the_key_is_encrypted_and_write_only()
    {
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("member@kutax.test", Password);
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/ai-settings", Ct)).StatusCode);
        using var member = await Login(factory, "member@kutax.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/ai-settings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync("/api/admin/ai-settings", Settings(apiKey: SyntheticKey), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/admin/ai-settings/models", null, Ct)).StatusCode);

        using var admin = await Login(factory, "admin@kutax.test");
        var initial = await admin.GetFromJsonAsync<JsonElement>("/api/admin/ai-settings", Ct);
        Assert.Equal("environment", initial.GetProperty("source").GetString());
        Assert.False(initial.GetProperty("apiKeySet").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(apiKey: SyntheticKey, model: "bad model; drop"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/ai-settings/models", null, Ct)).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(apiKey: SyntheticKey), Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(admin);

        var saved = await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(apiKey: SyntheticKey), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(SyntheticKey, body);
        var described = JsonDocument.Parse(body).RootElement;
        Assert.Equal("database", described.GetProperty("source").GetString());
        Assert.True(described.GetProperty("configured").GetBoolean());
        Assert.Equal("…1234", described.GetProperty("apiKeyHint").GetString());
        Assert.DoesNotContain(SyntheticKey, await admin.GetStringAsync("/api/admin/ai-settings", Ct));
        using (var scope = factory.Services.CreateScope())
        {
            var record = await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().AiSettings.SingleAsync(Ct);
            Assert.NotNull(record.ProtectedApiKey);
            Assert.DoesNotContain(SyntheticKey, record.ProtectedApiKey);
            Assert.Equal(SyntheticKey, (await scope.ServiceProvider.GetRequiredService<IAiOptionsSource>().GetAsync(Ct)).ApiKey);
        }

        // Leaving the key blank keeps it; clearing it while AI is on is rejected.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(model: "synthetic/vision-2"), Ct)).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/admin/ai-settings", Ct)).GetProperty("apiKeySet").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(clearApiKey: true), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(enabled: false, clearApiKey: true), Ct)).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/admin/ai-settings", Ct)).GetProperty("apiKeySet").GetBoolean());
    }

    [Fact]
    public async Task Changing_models_invalidates_existing_user_consent()
    {
        using var factory = new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        using var admin = await Login(factory, "admin@kutax.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(apiKey: SyntheticKey), Ct)).StatusCode);
        var privacy = await admin.GetFromJsonAsync<JsonElement>("/api/privacy", Ct);
        Assert.Equal("Groq", privacy.GetProperty("provider").GetString());
        Assert.Equal("synthetic/text", privacy.GetProperty("textModel").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/privacy", new { externalAiConsent = true, noticeVersion = privacy.GetProperty("noticeVersion").GetString() }, Ct)).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/privacy", Ct)).GetProperty("externalAiConsent").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/ai-settings", Settings(textModel: "synthetic/other-text"), Ct)).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/privacy", Ct)).GetProperty("externalAiConsent").GetBoolean());
    }
}
