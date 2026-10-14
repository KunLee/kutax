using System.Net;
using System.Net.Http.Json;
using KuTax.Core.Claims;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;
public sealed class RuleReviewTests
{
    private const string Password = "Synthetic-Rules-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> Login(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }

    [Fact]
    public async Task An_administrator_can_supersede_an_earlier_review_and_the_previous_one_is_audited()
    {
        using var factory = new ApiFactory();
        var first = await factory.CreateAdministratorAsync("first-admin@kutax.test", Password);
        var second = await factory.CreateAdministratorAsync("second-admin@kutax.test", Password);
        await factory.CreateUserAsync("member@kutax.test", Password);
        var version = TaxRuleCatalog.Rules[0].Version;
        var url = $"/api/tax-rules/{version}/review";

        using var member = await Login(factory, "member@kutax.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(url, new { confirm = true, note = "Not allowed", supersede = true }, Ct)).StatusCode);

        using var firstAdmin = await Login(factory, "first-admin@kutax.test");
        Assert.Equal(HttpStatusCode.NoContent, (await firstAdmin.PostAsJsonAsync(url, new { confirm = true, note = "Synthetic first review" }, Ct)).StatusCode);
        using var secondAdmin = await Login(factory, "second-admin@kutax.test");
        Assert.Equal(HttpStatusCode.Conflict, (await secondAdmin.PostAsJsonAsync(url, new { confirm = true, note = "Without supersede" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await secondAdmin.PostAsJsonAsync(url, new { confirm = true, note = "Checked against ATO sources myself", supersede = true }, Ct)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        var review = await db.TaxRuleReviews.SingleAsync(r => r.RuleVersion == version, Ct);
        Assert.Equal((second.Id, "Checked against ATO sources myself"), (review.ActorId, review.Note));
        var audit = await db.AuditEvents.SingleAsync(a => a.Action == "TaxRuleReviewSuperseded", Ct);
        Assert.Contains("Synthetic first review", audit.DetailsJson);
        Assert.Contains(first.Id, audit.DetailsJson);
    }
}
