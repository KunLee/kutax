using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Processing;

namespace KuTax.Api.Features.Ai;

/// <summary>Administrator management of the workspace AI provider. The API key is write-only.</summary>
public static partial class AiSettingsEndpoints
{
    public static void MapAiSettings(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/ai-settings").RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator));
        group.MapGet("/", async (AiSettingsStore store, CancellationToken ct) => Results.Ok(await Describe(store, ct)));
        group.MapPut("/", async (AiSettingsRequest request, ClaimsPrincipal principal, AiSettingsStore store, ILoggerFactory logs, CancellationToken ct) =>
        {
            var provider = AiPrivacy.Providers.FirstOrDefault(p => string.Equals(p, request.Provider?.Trim(), StringComparison.OrdinalIgnoreCase));
            var model = request.Model?.Trim() ?? "";
            var textModel = request.TextModel?.Trim() ?? "";
            var region = request.Region?.Trim() ?? "";
            var notice = request.RetentionNotice?.Trim() ?? "";
            var key = request.ApiKey?.Trim();
            if (provider is null || !ModelId().IsMatch(model) || !ModelId().IsMatch(textModel) || region.Length > 500 || notice.Length > 2000
                || key is { Length: > 500 } || key is not null && key.Any(char.IsWhiteSpace))
                return Results.Problem("Choose Groq or OpenAI and enter valid model IDs, region and retention notice.", statusCode: 400);
            var current = await store.GetAsync(ct);
            var keyAvailable = !request.ClearApiKey && (!string.IsNullOrEmpty(key) || !string.IsNullOrWhiteSpace(current.ApiKey));
            if (request.Enabled && (!keyAvailable || model.Length == 0 || provider == "Groq" && textModel.Length == 0 || region.Length == 0 || notice.Length == 0))
                return Results.Problem("To turn AI on, add an API key, the model(s), the processing region and the retention notice.", statusCode: 400);
            var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await store.SaveAsync(new AiSettingsUpdate(request.Enabled, provider, key, request.ClearApiKey, model, textModel, region, notice), actor, ct);
            logs.CreateLogger("KuTax.AiSettings").LogInformation("AI settings updated by {ActorId}; provider {Provider}, enabled {Enabled}.", actor, provider, request.Enabled);
            return Results.Ok(await Describe(store, ct));
        });
        // Lists the models the saved key can use. Doubles as a key check; the key never leaves the server.
        group.MapPost("/models", async (AiSettingsStore store, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var options = await store.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(options.ApiKey)) return Results.Problem("Save an API key first.", statusCode: 400);
            using var request = new HttpRequestMessage(HttpMethod.Get, AiPrivacy.ModelsEndpoint(options));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            try
            {
                using var response = await clients.CreateClient("ai-models").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                    return Results.Problem($"{AiPrivacy.ProviderName(options)} rejected the request (HTTP {(int)response.StatusCode}). Check the key.", statusCode: 502);
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var bounded = new MemoryStream();
                var buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    if (bounded.Length + count > 1024 * 1024) return Results.Problem("The model list was too large.", statusCode: 502);
                    await bounded.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                using var json = JsonDocument.Parse(bounded.ToArray());
                var models = json.RootElement.GetProperty("data").EnumerateArray()
                    .Where(m => !m.TryGetProperty("active", out var active) || active.ValueKind != JsonValueKind.False)
                    .Select(m => m.GetProperty("id").GetString()).OfType<string>().Where(id => ModelId().IsMatch(id)).Order().ToArray();
                return Results.Ok(new { models });
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return Results.Problem($"{AiPrivacy.ProviderName(options)} could not be reached. Try again shortly.", statusCode: 502);
            }
        });
    }

    private static async Task<object> Describe(AiSettingsStore store, CancellationToken ct)
    {
        var saved = await store.SavedAsync(ct);
        var options = await store.GetAsync(ct);
        return new
        {
            source = saved is null ? "environment" : "database",
            enabled = options.Enabled,
            provider = AiPrivacy.IsGroq(options) ? "Groq" : "OpenAI",
            model = options.Model ?? "",
            textModel = options.TextModel ?? "",
            region = options.Region ?? "",
            retentionNotice = options.RetentionNotice ?? "",
            apiKeySet = !string.IsNullOrWhiteSpace(options.ApiKey),
            apiKeyHint = saved?.ApiKeyHint ?? AiSettingsStore.Hint(options.ApiKey),
            apiKeyUnreadable = saved?.ProtectedApiKey is not null && options.ApiKey is null,
            configured = AiPrivacy.Configured(options),
            endpoint = AiPrivacy.Endpoint(options),
            noticeVersion = AiPrivacy.NoticeVersion(options),
            updatedAt = saved?.UpdatedAt,
            providers = AiPrivacy.Providers,
        };
    }

    [GeneratedRegex("^[A-Za-z0-9._:/-]{0,200}$")]
    private static partial Regex ModelId();
}

public sealed record AiSettingsRequest(bool Enabled, string? Provider, string? ApiKey, bool ClearApiKey, string? Model, string? TextModel, string? Region, string? RetentionNotice);
