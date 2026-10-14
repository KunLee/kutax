using System.Security.Cryptography;
using System.Text;

namespace KuTax.Infrastructure.Processing;
public static class AiPrivacy
{
    public static readonly string[] Providers = ["Groq", "OpenAI"];
    public static bool IsGroq(AiOptions options)=>string.Equals(options.Provider,"Groq",StringComparison.OrdinalIgnoreCase);
    public static string ProviderName(AiOptions options)=>IsGroq(options)?"Groq":"OpenAI Responses";
    public static string PrivacyUrl(AiOptions options)=>IsGroq(options)?"https://console.groq.com/docs/your-data":"https://developers.openai.com/api/docs/guides/your-data";
    public static string Endpoint(AiOptions options)=>!string.IsNullOrWhiteSpace(options.Endpoint)?options.Endpoint
        :IsGroq(options)?"https://api.groq.com/openai/v1/chat/completions":"https://api.openai.com/v1/responses";
    /// <summary>Model listing endpoint on the same host as the configured extraction endpoint.</summary>
    public static Uri ModelsEndpoint(AiOptions options)=>new(new Uri(Endpoint(options)),IsGroq(options)?"/openai/v1/models":"/v1/models");
    // Provider/model/location/retention changes require fresh consent; API-key rotation does not.
    public static string NoticeVersion(AiOptions options)
    {
        var parts=new List<string?>{Endpoint(options),options.Model,options.Region,options.RetentionNotice};
        // Groq reads PDF text with a separate model, which is part of what the user consents to.
        if(IsGroq(options))parts.Add(options.TextModel);
        return "ai-privacy-v1-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',parts))));
    }
    public static bool Configured(AiOptions options)=>options.Enabled
        &&!string.IsNullOrWhiteSpace(options.ApiKey)&&!string.IsNullOrWhiteSpace(options.Model)
        &&(!IsGroq(options)||!string.IsNullOrWhiteSpace(options.TextModel))
        &&!string.IsNullOrWhiteSpace(options.Region)&&!string.IsNullOrWhiteSpace(options.RetentionNotice)
        &&Uri.TryCreate(Endpoint(options),UriKind.Absolute,out var endpoint)&&endpoint.Scheme=="https";
}
