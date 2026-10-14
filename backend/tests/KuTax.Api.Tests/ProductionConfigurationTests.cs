using KuTax.Api.Setup;
using Microsoft.Extensions.Configuration;

namespace KuTax.Api.Tests;
public sealed class ProductionConfigurationTests
{
    [Theory]
    [InlineData("PublicOrigin","http://localhost")]
    [InlineData("AllowedHosts","*")]
    [InlineData("DataProtection:KeyPath","relative")]
    [InlineData("Storage:SecretKey","short")]
    [InlineData("Scanning:Host","")]
    [InlineData("Production:PrivateServiceNetwork","false")]
    public void Insecure_production_configuration_is_rejected(string key,string value)
    {
        var settings=Valid();settings[key]=value;Assert.Throws<InvalidOperationException>(()=>ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
    }
    [Fact]
    public void Explicit_https_private_network_and_protected_secrets_are_accepted()=>ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(Valid()).Build());
    [Theory]
    [InlineData("DataProtection:CertificateBase64","")]
    [InlineData("ConnectionStrings:Database","Host=db.supabase.test;Password=Synthetic-Configuration-Only-481!;SSL Mode=Disable")]
    [InlineData("Storage:Endpoint","http://storage.example.test")]
    [InlineData("Scanning:Host","clamav.example.test")]
    public void Managed_hosting_needs_encrypted_keys_tls_services_and_the_scanner_beside_the_app(string key,string value)
    {
        var settings=Managed();settings[key]=value;Assert.Throws<InvalidOperationException>(()=>ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
    }
    [Fact]
    public void Managed_hosting_with_tls_services_and_a_sidecar_scanner_is_accepted()=>ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(Managed()).Build());
    /// <summary>Cloud Run with Supabase, Cloudflare R2 and ClamAV as a sidecar.</summary>
    private static Dictionary<string,string?> Managed()=>new(){["PublicOrigin"]="https://tax.example.test",["AllowedHosts"]="tax.example.test",["DataProtection:Store"]="Database",["DataProtection:CertificateBase64"]="c3ludGhldGlj",
        ["ConnectionStrings:Database"]="Host=db.supabase.test;Password=Synthetic-Configuration-Only-481!;SSL Mode=Require",["Storage:Endpoint"]="https://account.r2.example.test",["Storage:AccessKey"]="synthetic-access-key-481",
        ["Storage:SecretKey"]="synthetic-secret-key-only-481",["Scanning:Host"]="localhost",["Production:ManagedServices"]="true"};
    private static Dictionary<string,string?> Valid()=>new(){["PublicOrigin"]="https://tax.example.test",["AllowedHosts"]="tax.example.test",["DataProtection:KeyPath"]=Path.Combine(Path.GetTempPath(),"kutax-keys"),["ConnectionStrings:Database"]="Host=postgres;Password=Synthetic-Configuration-Only-481!",["Storage:AccessKey"]="synthetic-access-key-481",["Storage:SecretKey"]="synthetic-secret-key-only-481",["Scanning:Host"]="clamav",["Production:PrivateServiceNetwork"]="true"};
}
