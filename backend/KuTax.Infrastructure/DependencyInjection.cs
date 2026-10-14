using Amazon.Runtime;
using Amazon.S3;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Storage;
using KuTax.Infrastructure.Processing;
using KuTax.Core.Claims;
using KuTax.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KuTax.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<KuTaxDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Database via user secrets or environment.")));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.Endpoint, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https", "Storage endpoint must be HTTP(S).")
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = options.Endpoint, ForcePathStyle = true,
                    AuthenticationRegion = options.Region, Timeout = TimeSpan.FromSeconds(10), MaxErrorRetry = 1,
                    // KuTax verifies each object's SHA-256 itself; extra SDK checksums are sent only where the S3 API
                    // requires them, which S3-compatible stores such as RustFS and Cloudflare R2 all accept.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED, ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
                });
        });
        services.AddSingleton<StorageSetup>();
        services.AddSingleton<IDocumentStorage, S3DocumentStorage>();
        services.AddSingleton<IDocumentScanner, ClamAvScanner>();
        services.AddSingleton<IAnnualReportGenerator, AnnualReportGenerator>();
        services.AddScoped<IExchangeRateSource, RbaExchangeRates>();
        services.AddHttpClient("rba", client => { client.Timeout = TimeSpan.FromSeconds(20); client.DefaultRequestHeaders.UserAgent.ParseAdd("KuTax/1.0"); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<AiSettingsStore>();
        services.AddScoped<IAiOptionsSource>(sp => sp.GetRequiredService<AiSettingsStore>());
        services.AddHttpClient("ai-models", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IReceiptExtractor, ReceiptExtractor>(client=>client.Timeout=TimeSpan.FromSeconds(75))
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
        services.AddHttpClient(Rules.AtoSources.Client, client => { client.Timeout = TimeSpan.FromMinutes(3); client.DefaultRequestHeaders.UserAgent.ParseAdd("KuTax/1.0"); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<Rules.IAtoSources, Rules.AtoSources>();
        services.AddScoped<Rules.RulePackService>();
        services.AddHttpClient<Rules.IRulePackDrafter, Rules.RulePackDrafter>(client=>client.Timeout=TimeSpan.FromSeconds(120))
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
        services.AddHttpClient<ICategorySuggester, CategorySuggester>(client=>client.Timeout=TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
        return services;
    }
}
