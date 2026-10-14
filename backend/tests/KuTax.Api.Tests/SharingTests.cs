using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KuTax.Api.Features.Sharing;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KuTax.Api.Tests;

/// <summary>Only people someone explicitly shared with, and who accepted, can act on their records — and only until it ends.</summary>
public sealed class SharingTests
{
    private const string Password = "Synthetic-Sharing-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> Login(WebApplicationFactoryClient factory, string email)
    {
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }

    private static HttpRequestMessage AsOwner(HttpMethod method, string url, string ownerId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(Taxpayer.Header, ownerId);
        return request;
    }

    [Fact]
    public async Task Access_starts_only_when_the_invited_person_accepts_and_stops_when_sharing_ends()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserAsync("owner@kutax.test", Password);
        var helper = await factory.CreateUserAsync("Helper@KuTax.test", Password);
        var stranger = await factory.CreateUserAsync("stranger@kutax.test", Password);
        ReceiptTransaction receipt;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            var batch = new ImportBatch { OwnerId = owner.Id, ExpectedFiles = 1 };
            var document = new ReceiptDocument { OwnerId = owner.Id, ImportBatchId = batch.Id, OriginalFileName = "r.pdf", RelativePath = "r.pdf", ObjectKey = "k", Sha256 = "s", ContentType = "application/pdf", ScanStatus = "Clean", Status = "NeedsReview" };
            receipt = new ReceiptTransaction { DocumentId = document.Id, OwnerId = owner.Id, Merchant = "Synthetic Shop", Date = new(2025, 9, 1), Amount = 20m };
            db.AddRange(batch, document, receipt, new ProcessingJob { DocumentId = document.Id, OwnerId = owner.Id, Status = "Complete" });
            await db.SaveChangesAsync(Ct);
        }
        var factoryClient = new WebApplicationFactoryClient(factory);
        using var ownerClient = await Login(factoryClient, "owner@kutax.test");
        using var helperClient = await Login(factoryClient, "helper@kutax.test");
        using var strangerClient = await Login(factoryClient, "stranger@kutax.test");
        object Skip(Guid version) => new { version, dismiss = true, reason = "Personal" };

        // Invited (the address matches whatever its case), but not yet accepted: acting for the owner is refused.
        var invite = await (await ownerClient.PostAsJsonAsync("/api/sharing/invite", new { email = "helper@kutax.test" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(invite.GetProperty("emailed").GetBoolean()); // no email server in tests: the link is shown to share instead
        Assert.StartsWith("https://localhost/invite/", invite.GetProperty("link").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await helperClient.SendAsync(AsOwner(HttpMethod.Put, $"/api/transactions/{receipt.Id}/dismiss", owner.Id, Skip(receipt.Version)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PostAsJsonAsync("/api/sharing/invite", new { email = "HELPER@kutax.test" }, Ct)).StatusCode);

        // The invitation shows up for the invited person only; someone else can't accept it.
        var mine = await helperClient.GetFromJsonAsync<JsonElement>("/api/sharing", Ct);
        var invitation = Assert.Single(mine.GetProperty("invitations").EnumerateArray());
        Assert.True(invitation.GetProperty("canAcceptHere").GetBoolean()); // the account existed before the invitation
        Assert.Empty((await strangerClient.GetFromJsonAsync<JsonElement>("/api/sharing", Ct)).GetProperty("invitations").EnumerateArray());
        var id = invitation.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await strangerClient.PostAsync($"/api/sharing/{id}/accept", null, Ct)).StatusCode);

        // Accepted: the helper works on the owner's records, and the change is recorded as theirs.
        Assert.Equal(HttpStatusCode.OK, (await helperClient.PostAsync($"/api/sharing/{id}/accept", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await helperClient.PutAsJsonAsync($"/api/transactions/{receipt.Id}/dismiss", Skip(receipt.Version), Ct)).StatusCode); // not as themselves
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.SendAsync(AsOwner(HttpMethod.Put, $"/api/transactions/{receipt.Id}/dismiss", owner.Id, Skip(receipt.Version)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await helperClient.SendAsync(AsOwner(HttpMethod.Put, $"/api/transactions/{receipt.Id}/dismiss", owner.Id, Skip(receipt.Version)), Ct)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var audit = await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().AuditEvents.SingleAsync(e => e.Action == "ReceiptNotClaimed", Ct);
            Assert.Equal((owner.Id, helper.Id), (audit.OwnerId, audit.ActorId));
        }
        var activity = await ownerClient.GetFromJsonAsync<JsonElement>("/api/sharing/activity", Ct);
        Assert.Contains(activity.EnumerateArray(), e => e.GetProperty("action").GetString() == "ReceiptNotClaimed" && e.GetProperty("person").GetProperty("email").GetString() == "Helper@KuTax.test");
        var shared = await helperClient.GetFromJsonAsync<JsonElement>("/api/sharing", Ct);
        Assert.Equal(owner.Id, Assert.Single(shared.GetProperty("incoming").EnumerateArray()).GetProperty("ownerId").GetString());

        // Ended by the owner: refused straight away.
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/api/sharing/{id}/end", null, Ct)).StatusCode);
        var after = await helperClient.SendAsync(AsOwner(HttpMethod.Put, $"/api/transactions/{receipt.Id}/dismiss", owner.Id, Skip(receipt.Version)), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        Assert.Equal("DelegationEnded", (await after.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Typing_an_address_suggests_people_shared_with_before_then_other_accounts()
    {
        using var factory = new ApiFactory();
        await factory.CreateUserAsync("owner@kutax.test", Password);
        await factory.CreateUserAsync("helper@kutax.test", Password);
        await factory.CreateUserAsync("hannah@kutax.test", Password);
        using var owner = await Login(new WebApplicationFactoryClient(factory), "owner@kutax.test");
        async Task<string[]> Suggest(string q) => [.. (await owner.GetFromJsonAsync<JsonElement>($"/api/sharing/people?q={Uri.EscapeDataString(q)}", Ct))
            .EnumerateArray().Select(p => p.GetProperty("email").GetString()! + (p.GetProperty("known").GetBoolean() ? "*" : ""))];

        Assert.Empty(await Suggest(""));
        Assert.Empty(await Suggest("h")); // accounts only after two characters
        Assert.Equal(["helper@kutax.test"], await Suggest("he"));
        Assert.Empty(await Suggest("owner")); // never yourself

        // Shared before (now ended): suggested first, even before typing; someone shared with right now isn't.
        var invite = await owner.PostAsJsonAsync("/api/sharing/invite", new { email = "helper@kutax.test" }, Ct);
        Assert.Empty(await Suggest(""));
        var id = (await owner.GetFromJsonAsync<JsonElement>("/api/sharing", Ct)).GetProperty("outgoing")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/sharing/{id}/end", null, Ct)).StatusCode);
        Assert.Equal(["helper@kutax.test*"], await Suggest(""));
        Assert.Equal(["helper@kutax.test*", "hannah@kutax.test"], await Suggest("@kutax"));
    }

    [Fact]
    public async Task An_account_made_after_the_invitation_accepts_only_through_the_emailed_link()
    {
        using var root = new ApiFactory();
        var owner = await root.CreateUserAsync("owner@kutax.test", Password);
        using var factory = root.WithWebHostBuilder(builder => builder.UseSetting("Registration:Enabled", "false"));
        var factoryClient = new WebApplicationFactoryClient(factory);
        using var ownerClient = await Login(factoryClient, "owner@kutax.test");
        var link = (await (await ownerClient.PostAsJsonAsync("/api/sharing/invite", new { email = "new@kutax.test" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = link[(link.LastIndexOf('/') + 1)..];

        // The link page shows who it's from; open registration is off, but the invited address may still sign up with it.
        using var anonymous = factory.CreateClient();
        var about = await anonymous.GetFromJsonAsync<JsonElement>($"/api/invitations/{token}", Ct);
        Assert.Equal(("owner@kutax.test", "new@kutax.test", "Pending"), (about.GetProperty("from").GetProperty("email").GetString(), about.GetProperty("email").GetString(), about.GetProperty("state").GetString()));
        await AuthenticationTests.RefreshCsrfAsync(anonymous);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/auth/register", new { email = "other@kutax.test", displayName = "Other", password = Password, inviteToken = token }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await anonymous.PostAsJsonAsync("/api/auth/register", new { email = "new@kutax.test", displayName = "New", password = Password, inviteToken = token }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(anonymous);

        // A brand-new account can't accept in the app (anyone could have signed up with that address); the link works.
        var invitation = Assert.Single((await anonymous.GetFromJsonAsync<JsonElement>("/api/sharing", Ct)).GetProperty("invitations").EnumerateArray());
        Assert.False(invitation.GetProperty("canAcceptHere").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await anonymous.PostAsync($"/api/sharing/{invitation.GetProperty("id").GetGuid()}/accept", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/sharing/accept-link", new { token }, Ct)).StatusCode);
        // Single use.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/sharing/accept-link", new { token }, Ct)).StatusCode);
        Assert.Equal(owner.Id, Assert.Single((await anonymous.GetFromJsonAsync<JsonElement>("/api/sharing", Ct)).GetProperty("incoming").EnumerateArray()).GetProperty("ownerId").GetString());
    }

    /// <summary>Lets one helper sign in against either a factory or one derived from it.</summary>
    private sealed class WebApplicationFactoryClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        public HttpClient CreateClient() => factory.CreateClient();
    }
}
