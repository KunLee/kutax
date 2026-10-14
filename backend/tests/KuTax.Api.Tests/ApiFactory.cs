using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KuTax.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string environment;

    public ApiFactory(string environment = "Development")
    {
        this.environment = environment;
        connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Database=unused");
        builder.UseSetting("Storage:AccessKey", "synthetic-test-key");
        builder.UseSetting("Storage:SecretKey", "synthetic-test-secret");
        builder.UseSetting("Processing:Enabled", "false");
        builder.UseSetting("PublicOrigin","https://localhost");
        builder.UseSetting("AllowedHosts","localhost");
        builder.UseSetting("DataProtection:KeyPath",Path.Combine(Path.GetTempPath(),"kutax-test-keys-"+Guid.NewGuid().ToString("N")));
        builder.UseSetting("Production:PrivateServiceNetwork","true");
        builder.UseSetting("Scanning:Host","localhost");
        if(environment=="Production")
        {
            builder.UseSetting("ConnectionStrings:Database","Host=localhost;Database=unused;Password=Synthetic-Production-Test-Only-481!");
            builder.UseSetting("Storage:AccessKey","synthetic-production-access-key");
            builder.UseSetting("Storage:SecretKey","synthetic-production-secret-key-481");
        }
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<KuTaxDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<KuTaxDbContext>>();
            services.AddDbContext<KuTaxDbContext>(options => options.UseSqlite(connection));
            // Tests never download live RBA rates; they seed the rates they need.
            services.AddHttpClient("rba").ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
        });
    }

    public async Task<ApplicationUser> CreateUserAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().Database.EnsureCreatedAsync();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        return user;
    }

    public async Task<ApplicationUser> CreateAdministratorAsync(string email, string password)
    {
        var user = await CreateUserAsync(email, password);
        using var scope = Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(ApplicationRoles.Administrator))
            Assert.True((await roles.CreateAsync(new IdentityRole(ApplicationRoles.Administrator))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var trackedUser = (await users.FindByIdAsync(user.Id))!;
        Assert.True((await users.AddToRoleAsync(trackedUser, ApplicationRoles.Administrator)).Succeeded);
        return trackedUser;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

internal sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
}
