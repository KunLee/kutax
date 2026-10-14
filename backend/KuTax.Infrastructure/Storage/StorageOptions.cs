using System.ComponentModel.DataAnnotations;

namespace KuTax.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    [Required, Url] public string Endpoint { get; set; } = "http://localhost:9000";
    [Required] public string AccessKey { get; set; } = "";
    [Required] public string SecretKey { get; set; } = "";
    [Required, RegularExpression("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")]
    public string Bucket { get; set; } = "kutax-private";
    /// <summary>Signing region: any value suits RustFS; Cloudflare R2 expects "auto".</summary>
    [Required, RegularExpression("^[a-z0-9-]{2,30}$")]
    public string Region { get; set; } = "us-east-1";
}
