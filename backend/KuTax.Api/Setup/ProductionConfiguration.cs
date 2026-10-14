using Npgsql;

namespace KuTax.Api.Setup;
public static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        if(!Uri.TryCreate(configuration["PublicOrigin"],UriKind.Absolute,out var origin)||origin.Scheme!="https"||origin.AbsolutePath!="/")throw new InvalidOperationException("Production requires an HTTPS PublicOrigin.");
        var hosts=(configuration["AllowedHosts"]??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(hosts.Length==0||hosts.Any(h=>h.Contains('*'))||!hosts.Contains(origin.Host,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Production AllowedHosts must explicitly include the public host without wildcards.");
        // Keys on a persistent disk, or in the database encrypted with a certificate from the host's secret store.
        if(configuration["DataProtection:Store"]=="Database")
        {
            if(configuration["DataProtection:CertificateBase64"] is not{Length:>0})throw new InvalidOperationException("Production keys stored in the database must be encrypted with a certificate (DataProtection:CertificateBase64).");
        }
        else if(configuration["DataProtection:KeyPath"] is not{Length:>0} path||!Path.IsPathFullyQualified(path))throw new InvalidOperationException("Production requires an absolute persistent data-protection key path.");
        var database=new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database"));
        if(database.Password is not{Length:>=24}||configuration["Storage:AccessKey"] is not{Length:>=16}||configuration["Storage:SecretKey"] is not{Length:>=24})throw new InvalidOperationException("Production requires independently generated protected database and storage credentials.");
        if(string.IsNullOrWhiteSpace(configuration["Scanning:Host"]))throw new InvalidOperationException("Production requires a private malware-scanning endpoint.");
        // Either everything sits on a private network (self-hosted), or managed services are reached over TLS with the
        // scanner beside the app in the same instance (Cloud Run sidecar).
        if(configuration.GetValue<bool>("Production:ManagedServices"))
        {
            if(database.SslMode is not(SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))throw new InvalidOperationException("Managed production requires a TLS database connection (SSL Mode=Require or stricter).");
            if(!Uri.TryCreate(configuration["Storage:Endpoint"],UriKind.Absolute,out var storage)||storage.Scheme!="https")throw new InvalidOperationException("Managed production requires an HTTPS storage endpoint.");
            if(configuration["Scanning:Host"] is not("localhost" or "127.0.0.1"))throw new InvalidOperationException("Managed production requires the malware scanner beside the app (Scanning:Host=localhost).");
        }
        else if(!configuration.GetValue<bool>("Production:PrivateServiceNetwork"))throw new InvalidOperationException("Production database, storage and scanner endpoints must be on a private service network.");
    }
}
