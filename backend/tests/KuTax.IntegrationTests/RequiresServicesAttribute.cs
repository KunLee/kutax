using System.Runtime.CompilerServices;

namespace KuTax.IntegrationTests;

public sealed class RequiresServicesAttribute : FactAttribute
{
    public RequiresServicesAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.GetEnvironmentVariable("KUTAX_INTEGRATION") != "1")
            Skip = "Requires PostgreSQL, RustFS and ClamAV. Set KUTAX_INTEGRATION=1 and service configuration; see README.";
    }
}
public sealed class RequiresIsolatedServicesAttribute : FactAttribute
{
    public RequiresIsolatedServicesAttribute([CallerFilePath]string? sourceFilePath=null,[CallerLineNumber]int sourceLineNumber=-1):base(sourceFilePath,sourceLineNumber)
    {
        if(Environment.GetEnvironmentVariable("KUTAX_INTEGRATION")!="1")Skip="Requires real PostgreSQL, RustFS and ClamAV services.";
        else if(Environment.GetEnvironmentVariable("KUTAX_APP_URL") is not null)Skip="Scanner-worker isolation is tested with the application stopped; run the isolated service gate first.";
    }
}
