using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace KuTax.Api.Tests;

/// <summary>Behind the Cloudflare Worker, only requests carrying the shared secret are served.</summary>
public sealed class OriginVerifyTests
{
    private const string Secret = "synthetic-origin-secret-for-tests-only-481";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requests_without_the_worker_secret_are_refused()
    {
        using var root = new ApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.UseSetting("Origin:SharedSecret", Secret));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/health/live", Ct)).StatusCode);
        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/health/live");
        wrong.Headers.Add("X-KuTax-Origin-Verify", Secret[..^1] + "x");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrong, Ct)).StatusCode);

        using var right = new HttpRequestMessage(HttpMethod.Get, "/api/health/live");
        right.Headers.Add("X-KuTax-Origin-Verify", Secret);
        right.Headers.Add("X-KuTax-Client-IP", "203.0.113.7");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(right, Ct)).StatusCode);
    }

    [Fact]
    public async Task Without_a_configured_secret_nothing_changes()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health/live", Ct)).StatusCode);
    }
}
