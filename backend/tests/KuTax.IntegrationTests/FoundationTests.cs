using System.Net;
using System.Net.Http.Json;
using Amazon.S3;
using Amazon.S3.Model;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KuTax.IntegrationTests;

public sealed class FoundationTests
{
    [RequiresServices]
    public async Task Postgres_migrations_and_authenticated_session_work_against_real_services()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<KuTaxDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await services.GetRequiredService<StorageSetup>().InitialiseAsync();

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"integration-{Guid.NewGuid():N}@kutax.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        const string password = "Synthetic-Service-Test-481!";
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        try
        {
            using var client = factory.CreateClient();
            var csrf = await client.GetAsync("/api/auth/csrf");
            var cookie = csrf.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]));
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health/ready")).StatusCode);
        }
        finally { Assert.True((await users.DeleteAsync(user)).Succeeded); }
    }

    [RequiresServices]
    public async Task Rustfs_supports_signed_roundtrip_and_rejects_anonymous_object_access()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").UseSetting("Processing:Enabled","false"));
        var services = factory.Services;
        var storage = services.GetRequiredService<StorageSetup>();
        await storage.InitialiseAsync();
        await storage.CheckAsync(CancellationToken.None);
        var s3 = services.GetRequiredService<IAmazonS3>();
        var options = services.GetRequiredService<IOptions<StorageOptions>>().Value;
        var key = $"foundation-contract/{Guid.NewGuid():N}";
        const string content = "KuTax synthetic storage contract fixture. No financial records.";
        await s3.PutObjectAsync(new PutObjectRequest { BucketName = options.Bucket, Key = key, ContentBody = content, ContentType = "text/plain" });
        try
        {
            using var response = await s3.GetObjectAsync(options.Bucket, key);
            using var reader = new StreamReader(response.ResponseStream);
            Assert.Equal(content, await reader.ReadToEndAsync());
            using var anonymous = new HttpClient();
            var objectUrl = $"{options.Endpoint.TrimEnd('/')}/{options.Bucket}/{key}";
            Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(objectUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"{options.Endpoint.TrimEnd('/')}/{options.Bucket}?list-type=2")).StatusCode);
        }
        finally { await s3.DeleteObjectAsync(options.Bucket, key); }
    }
}
