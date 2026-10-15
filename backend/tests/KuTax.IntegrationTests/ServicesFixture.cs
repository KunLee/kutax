using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(KuTax.IntegrationTests.ServicesFixture))]

namespace KuTax.IntegrationTests;

/// <summary>
/// Prepares the real services once, before any test: the database is migrated and the private bucket exists. Tests
/// run in any order, so none of them may depend on another having set things up (CI starts from empty services).
/// </summary>
public sealed class ServicesFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("KUTAX_INTEGRATION") != "1") return;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").UseSetting("Processing:Enabled", "false"));
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<StorageSetup>().InitialiseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
