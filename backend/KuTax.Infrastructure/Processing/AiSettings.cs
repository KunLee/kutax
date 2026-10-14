using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KuTax.Infrastructure.Processing;

/// <summary>Workspace-wide AI provider settings saved by an administrator. A single row; the API key is encrypted.</summary>
public sealed class AiSettingsRecord
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Groq";
    public string Model { get; set; } = "";
    public string TextModel { get; set; } = "";
    public string Region { get; set; } = "";
    public string RetentionNotice { get; set; } = "";
    public string? ProtectedApiKey { get; set; }
    public string? ApiKeyHint { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = "";
}

/// <summary>Effective AI settings. The endpoint is server configuration only, so a key can never be redirected from the UI.</summary>
public sealed record AiOptions(bool Enabled, string Provider, string? ApiKey, string? Model, string? TextModel,
    string? Region, string? RetentionNotice, string? Endpoint)
{
    public static AiOptions FromConfiguration(IConfiguration config) => new(config.GetValue<bool>("Ai:Enabled"),
        config["Ai:Provider"] ?? "OpenAI", config["Ai:ApiKey"], config["Ai:Model"], config["Ai:TextModel"],
        config["Ai:Region"], config["Ai:RetentionNotice"], config["Ai:Endpoint"]);
}

public interface IAiOptionsSource
{
    Task<AiOptions> GetAsync(CancellationToken ct);
}

public sealed record AiSettingsUpdate(bool Enabled, string Provider, string? ApiKey, bool ClearApiKey, string Model,
    string TextModel, string Region, string RetentionNotice);

/// <summary>Saved settings win; until an administrator saves them, server configuration (infra/.env) applies.</summary>
public sealed class AiSettingsStore(KuTaxDbContext db, IDataProtectionProvider protection, IConfiguration config) : IAiOptionsSource
{
    private IDataProtector Protector => protection.CreateProtector("KuTax.AiSettings.ApiKey.v1");

    public Task<AiSettingsRecord?> SavedAsync(CancellationToken ct) =>
        db.AiSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == AiSettingsRecord.SingletonId, ct);

    public async Task<AiOptions> GetAsync(CancellationToken ct)
    {
        var saved = await SavedAsync(ct);
        if (saved is null) return AiOptions.FromConfiguration(config);
        string? key = null;
        if (saved.ProtectedApiKey is { } protectedKey)
        {
            // An unreadable key (for example after losing the data-protection keys) disables AI rather than failing every job.
            try { key = Protector.Unprotect(protectedKey); }
            catch (System.Security.Cryptography.CryptographicException) { key = null; }
        }
        return new AiOptions(saved.Enabled, saved.Provider, key, saved.Model, saved.TextModel, saved.Region, saved.RetentionNotice, config["Ai:Endpoint"]);
    }

    public async Task<AiSettingsRecord> SaveAsync(AiSettingsUpdate update, string actorId, CancellationToken ct)
    {
        var saved = await db.AiSettings.SingleOrDefaultAsync(s => s.Id == AiSettingsRecord.SingletonId, ct);
        if (saved is null)
        {
            // The first save adopts a key already supplied through server configuration unless a new one is given.
            var fallback = AiOptions.FromConfiguration(config);
            saved = new AiSettingsRecord();
            if (!string.IsNullOrWhiteSpace(fallback.ApiKey)) SetKey(saved, fallback.ApiKey);
            db.AiSettings.Add(saved);
        }
        if (update.ClearApiKey) { saved.ProtectedApiKey = null; saved.ApiKeyHint = null; }
        else if (!string.IsNullOrWhiteSpace(update.ApiKey)) SetKey(saved, update.ApiKey.Trim());
        saved.Enabled = update.Enabled;
        saved.Provider = update.Provider;
        saved.Model = update.Model.Trim();
        saved.TextModel = update.TextModel.Trim();
        saved.Region = update.Region.Trim();
        saved.RetentionNotice = update.RetentionNotice.Trim();
        saved.UpdatedAt = DateTimeOffset.UtcNow;
        saved.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);
        return saved;
    }

    public static string? Hint(string? key) => string.IsNullOrEmpty(key) ? null : "…" + key[^Math.Min(4, key.Length)..];

    private void SetKey(AiSettingsRecord saved, string key)
    {
        saved.ProtectedApiKey = Protector.Protect(key);
        saved.ApiKeyHint = Hint(key);
    }
}
