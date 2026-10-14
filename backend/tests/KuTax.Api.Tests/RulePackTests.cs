using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using KuTax.Infrastructure.Rules;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace KuTax.Api.Tests;
public sealed class RulePackTests
{
    private const string Password = "Synthetic-RulePack-481!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly HashSet<string> Known = ["amount", "workUsePercent", "reimbursement", "maxItemCost"];

    [Fact]
    public void Formulas_use_exact_decimals_and_reject_anything_unsafe_or_ill_typed()
    {
        var values = new Dictionary<string, decimal> { ["amount"] = 29.99m, ["workUsePercent"] = 80m, ["reimbursement"] = 0m, ["maxItemCost"] = 300m };
        Assert.Equal(23.99m, Formula.Parse("round2(amount * workUsePercent / 100) - reimbursement", Known).Number(values));
        Assert.Equal(0.3m, Formula.Parse("0.1 + 0.2", Known).Number(values));
        Assert.Equal(2.5m, Formula.Parse("round2(2.495)", Known).Number(values)); // midpoint away from zero
        Assert.True(Formula.Parse("amount <= maxItemCost and not (workUsePercent < 50 or amount == 0)", Known).Condition(values));
        Assert.Equal(300m, Formula.Parse("if(amount > maxItemCost, 0, max(amount, maxItemCost, 1))", Known).Number(values));
        Assert.Equal(-29.99m, Formula.Parse("-amount", Known).Number(values));
        foreach (var bad in new[] { "amount +", "amount > 1 + (2 > 1)", "unknownThing * 2", "round2(amount, 2)", "if(amount, 1, 2)", "amount and 1", "System.IO.File", "amount; drop", new string('(', 30) + "1" + new string(')', 30), string.Join("+", Enumerable.Repeat("1", 200)) })
            Assert.Throws<FormatException>(() => Formula.Parse(bad, Known));
        Assert.Throws<DivideByZeroException>(() => Formula.Parse("amount / reimbursement", Known).Number(values));
    }

    [Fact]
    public void Built_in_rules_are_valid_packs_and_validation_catches_broken_ones()
    {
        foreach (var year in new[] { 2024, 2025 }) Assert.Empty(RuleEngine.Validate(TaxRuleCatalog.BuiltInPack(year)));
        var pack = TaxRuleCatalog.BuiltInPack(2025);
        var tools = pack.Rules.Single(r => r.Category == "ToolsEquipment");
        // Pinned example parameters keep the examples valid when a year's limit changes.
        var raised = pack with { Rules = [tools with { Parameters = new() { ["maxItemCost"] = 400m, ["workUseAbove"] = 50m } }] };
        Assert.Empty(RuleEngine.Validate(raised));
        var broken = pack with { Rules = [tools with { Deductible = "round2(amount * workUsePercent / 10)" }, tools with { Category = "Personal", PaperReference = "D5E", SourceUrl = "https://example.com/" }] };
        var errors = RuleEngine.Validate(broken);
        Assert.Contains(errors, e => e.Contains("expected Candidate 180.00 but got Candidate 1800.00"));
        Assert.Contains(errors, e => e.Contains("invalid category name"));
        Assert.Contains(errors, e => e.Contains("paper reference"));
        Assert.Contains(errors, e => e.Contains("official ATO page"));
        Assert.Contains(RuleEngine.Validate(pack with { Rules = [tools with { Checks = [new("amount * 2", "Unsupported", "x")] }] }), e => e.Contains("must be a condition"));
    }

    [Fact]
    public void Reports_verify_quotes_against_the_source_and_list_field_changes()
    {
        var previous = TaxRuleCatalog.BuiltInPack(2025);
        var tools = previous.Rules.Single(r => r.Category == "ToolsEquipment");
        var draft = previous with { FinancialYearStart = 2026, Rules = [tools with { Parameters = new() { ["maxItemCost"] = 400m, ["workUseAbove"] = 50m },
            Evidence = [new("parameters.maxItemCost", "instructions", "an immediate deduction for the cost of depreciating assets costing $400 or less"),
                new("parameters.workUseAbove", "instructions", "if you use them more than 50% of the time for a work purpose")] }] };
        var source = new Dictionary<string, string> { ["instructions"] = "…the work-related portion of tools: an immediate deduction for the cost of depreciating\nassets costing $400 or less … if you use them more than 50% of the time for a work purpose." };
        var report = RulePackReview.Build(draft, previous, source, comparedWith: "FY 2025–26 rules");
        Assert.True(report.CanActivate, string.Join(" ", report.Errors));
        var limit = report.Fields.Single(f => f.Category == "ToolsEquipment" && f.Field == "parameters.maxItemCost");
        Assert.Equal(("300", "400", true, true), (limit.Previous, limit.Proposed, limit.Changed, limit.QuoteVerified));
        Assert.Contains(report.Warnings, w => w.Contains("UnionFees isn't in this pack"));
        // A quote that isn't in the source, or doesn't contain the number, blocks activation.
        var wrongNumber = draft with { Rules = [draft.Rules[0] with { Parameters = new() { ["maxItemCost"] = 500m, ["workUseAbove"] = 50m } }] };
        Assert.Contains(RulePackReview.Build(wrongNumber, previous, source).Errors, e => e.Contains("maxItemCost wasn't found in the official source with that number"));
        var invented = draft with { Rules = [draft.Rules[0] with { Evidence = [draft.Rules[0].Evidence[1], new("parameters.maxItemCost", "instructions", "items up to $400 are always deductible")] }] };
        Assert.False(RulePackReview.Build(invented, previous, source).CanActivate);
    }

    private sealed class FakeAto : IAtoSources
    {
        public static readonly string[] Pages = [
            "D3 Work-related clothing, laundry and dry-cleaning expenses. Write the total at question D3 – label C. C Compulsory work uniform N Non-compulsory work uniform S Occupation-specific clothing P Protective clothing",
            "D4 Work-related self-education expenses. Print your code (K or I) letter in the CLAIM TYPE box at question D4 – label D.",
            "D5 Other work-related expenses. union fees and subscriptions to trade, business or professional associations; an immediate deduction for the cost of depreciating assets costing $400 or less. Write the total at question D5 – label E.",
            "D9 Gifts or donations. Did you make a gift or donation of $2 or more to an organisation that has a deductible gift recipient (DGR) status? Write the total at question D9 – label J.",
            "D10 Cost of managing tax affairs. other expenses incurred in managing your tax affairs at label M."];
        public Task<bool> InstructionsPublishedAsync(int year, CancellationToken ct) => Task.FromResult(year == 2026);
        public Task<InstructionsDocument> InstructionsAsync(int year, CancellationToken ct) => Task.FromResult(new InstructionsDocument(year, TaxRuleCatalog.Instructions(year), new string('A', 64), Pages));
        public Task<string?> GuidanceTextAsync(string url, CancellationToken ct) => Task.FromResult<string?>("An immediate deduction is available for items that cost $400 or less, if you use them more than 50% of the time for a work purpose.");
    }

    /// <summary>Answers like a careful model: last year's shape, this year's values, verbatim quotes. The second reading of phone rules disagrees.</summary>
    private sealed class FakeModel(int quotaAt = 0) : HttpMessageHandler
    {
        private readonly Dictionary<string, int> calls = [];
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (++Requests == quotaAt)
            {
                // A daily quota: the provider asks for a long wait.
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
                return limited;
            }
            var body = await request.Content!.ReadAsStringAsync(ct);
            var input = JsonDocument.Parse(body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            var rule = JsonDocument.Parse(input[(input.IndexOf('\n') + 1)..input.IndexOf("\n\n", StringComparison.Ordinal)]).RootElement;
            var category = rule.GetProperty("Category").GetString()!;
            calls[category] = calls.GetValueOrDefault(category) + 1;
            var paper = rule.GetProperty("PaperReference").GetString()!;
            var question = paper.Split(' ')[0];
            var sectionQuote = FakeAto.Pages.First(p => p.StartsWith(question + " ", StringComparison.Ordinal)) is var page && page.Contains("label") ? page[page.IndexOf(question + " ", StringComparison.Ordinal)..].Split('.')[0] : question;
            var parameters = rule.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).Select(name => name switch
            {
                "maxItemCost" => new { name, value = 400m, quote = "an immediate deduction for the cost of depreciating assets costing $400 or less", source = "instructions" },
                "workUseAbove" => new { name, value = 50m, quote = "if you use them more than 50% of the time for a work purpose", source = "guidance" },
                _ => new { name, value = 2m, quote = "Did you make a gift or donation of $2 or more to an organisation", source = "instructions" },
            }).ToArray();
            var answer = new
            {
                stillApplies = true, label = rule.GetProperty("Label").GetString(), section = rule.GetProperty("Section").GetString(),
                paperReference = category == "PhoneInternet" && calls[category] == 2 ? "D5 F" : paper,
                claimTypes = rule.GetProperty("ClaimTypes").EnumerateArray().Select(c => c.GetString()).ToArray(),
                sectionQuote = new { quote = sectionQuote, source = "instructions" }, parameters,
                conditions = rule.GetProperty("Conditions").GetString(), declaration = rule.GetProperty("Declaration").GetString(),
                changes = category == "ToolsEquipment" ? new[] { "The immediate deduction limit rose from $300 to $400." } : [], methodChange = (string?)null,
            };
            var content = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = JsonSerializer.Serialize(answer) } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class FixedAi(AiOptions options) : IAiOptionsSource { public Task<AiOptions> GetAsync(CancellationToken ct) => Task.FromResult(options); }
    private static AiOptions Groq() => AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Ai:Enabled"] = "true", ["Ai:Provider"] = "Groq", ["Ai:ApiKey"] = "synthetic-groq-key", ["Ai:Model"] = "synthetic/vision", ["Ai:TextModel"] = "synthetic/text",
        ["Ai:Region"] = "Synthetic region", ["Ai:RetentionNotice"] = "Synthetic retention notice",
    }).Build());

    private static async Task<HttpClient> Login(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string email)
    {
        var client = factory.CreateClient();
        await AuthenticationTests.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
        await AuthenticationTests.RefreshCsrfAsync(client);
        return client;
    }

    [Fact]
    public async Task A_new_year_is_drafted_by_ai_checked_against_sources_and_only_used_after_activation()
    {
        using var root = new ApiFactory();
        await root.CreateAdministratorAsync("admin@kutax.test", Password);
        await root.CreateUserAsync("member@kutax.test", Password);
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAtoSources>(); services.AddScoped<IAtoSources, FakeAto>();
            services.RemoveAll<IAiOptionsSource>(); services.AddSingleton<IAiOptionsSource>(new FixedAi(Groq()));
            services.RemoveAll<IRulePackDrafter>();
            services.AddScoped<IRulePackDrafter>(sp => new RulePackDrafter(sp.GetRequiredService<IAiOptionsSource>(), sp.GetRequiredService<IAtoSources>(), new HttpClient(new FakeModel()), NullLogger<RulePackDrafter>.Instance));
        }));
        using (var member = await Login(factory, "member@kutax.test"))
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/rule-packs", Ct)).StatusCode);
        using var admin = await Login(factory, "admin@kutax.test");
        var status = await admin.GetFromJsonAsync<JsonElement>("/api/rule-packs", Ct);
        Assert.Equal(2026, status.GetProperty("nextYear").GetInt32());

        // The yearly check finds the 2027 instructions (FY 2026–27) and queues one draft; checking again doesn't queue another.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/rule-packs/check", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/rule-packs/check", null, Ct)).StatusCode);
        var queued = Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/rule-packs", Ct)).GetProperty("packs").EnumerateArray());
        Assert.Equal("Queued", queued.GetProperty("status").GetString());
        var worker = new RulePackWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RulePackWorker>.Instance);
        Assert.True(await worker.DraftOneAsync(Ct));
        Assert.False(await worker.DraftOneAsync(Ct));
        var packs = (await admin.GetFromJsonAsync<JsonElement>("/api/rule-packs", Ct)).GetProperty("packs").EnumerateArray().ToArray();
        var draft = Assert.Single(packs);
        Assert.Equal(("Draft", 0), (draft.GetProperty("status").GetString(), draft.GetProperty("errors").GetInt32()));

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/rule-packs/{draft.GetProperty("id").GetGuid()}", Ct);
        var report = detail.GetProperty("report");
        var limit = report.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("category").GetString() == "ToolsEquipment" && f.GetProperty("field").GetString() == "parameters.maxItemCost");
        Assert.Equal(("300", "400", true), (limit.GetProperty("previous").GetString(), limit.GetProperty("proposed").GetString(), limit.GetProperty("quoteVerified").GetBoolean()));
        var warnings = report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Contains(warnings, w => w.Contains("PhoneInternet: two readings disagree on the label"));
        Assert.Contains(warnings, w => w.Contains("rose from $300 to $400"));

        // Not in force before activation.
        using (var scope = factory.Services.CreateScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().LoadRulesAsync(Ct)).Find(2026, "ToolsEquipment"));
        var id = draft.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/rule-packs/{id}/activate", new { confirm = true, note = "" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/rule-packs/{id}/activate", new { confirm = true, note = "Synthetic check against fake sources" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/rule-packs/{id}/activate", new { confirm = true, note = "Again" }, Ct)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            var rules = await db.LoadRulesAsync(Ct);
            var tool = rules.Find(2026, "ToolsEquipment")!;
            Assert.Equal("AU-2026-ToolsEquipment-v1", tool.Version);
            Assert.True(await db.TaxRuleReviews.AnyAsync(r => r.RuleVersion == tool.Version, Ct)); // activation is the review
            var transaction = new ReceiptTransaction { Date = new(2026, 9, 1), Amount = 350m, Status = "Confirmed" };
            var allocation = new ExpenseAllocation { Category = "ToolsEquipment", Amount = 350m, WorkUsePercent = 100m, Basis = "b", EligibilityConfirmed = true };
            Assert.Equal(("Candidate", 350m), (TaxCalculator.Assess(transaction, allocation, tool, true, true).Status, TaxCalculator.Assess(transaction, allocation, tool, true, true).DeductibleAmount));
            Assert.Equal("Unsupported", TaxCalculator.Assess(transaction, allocation, rules.Find(2025, "ToolsEquipment"), true, true).Status);
        }
        Assert.Equal(2027, (await admin.GetFromJsonAsync<JsonElement>("/api/rule-packs", Ct)).GetProperty("nextYear").GetInt32());
    }

    [Fact]
    public async Task Working_on_a_year_only_checks_the_ato_for_a_newer_year_once_its_instructions_are_due()
    {
        using var root = new ApiFactory();
        await root.CreateAdministratorAsync("admin@kutax.test", Password);
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IAtoSources>(); services.AddScoped<IAtoSources, FakeAto>(); }));
        var worker = new RulePackWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RulePackWorker>.Instance);
        // FY 2025–26 already has rules; FY 2026–27's instructions aren't due until June 2027. Neither reaches out to the ATO.
        await worker.CheckYearAsync(2025, Ct);
        if (!RulePackWorker.InCheckWindow(2026, DateOnly.FromDateTime(DateTime.UtcNow))) await worker.CheckYearAsync(2026, Ct);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<KuTaxDbContext>().RuleSourceChecks.CountAsync(Ct));
    }

    [Fact]
    public async Task A_draft_interrupted_by_an_ai_quota_waits_and_resumes_without_repeating_work()
    {
        using var root = new ApiFactory();
        await root.CreateAdministratorAsync("admin@kutax.test", Password);
        var model = new FakeModel(quotaAt: 7);
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAtoSources>(); services.AddScoped<IAtoSources, FakeAto>();
            services.RemoveAll<IAiOptionsSource>(); services.AddSingleton<IAiOptionsSource>(new FixedAi(Groq()));
            services.RemoveAll<IRulePackDrafter>();
            services.AddScoped<IRulePackDrafter>(sp => new RulePackDrafter(sp.GetRequiredService<IAiOptionsSource>(), sp.GetRequiredService<IAtoSources>(), new HttpClient(model), NullLogger<RulePackDrafter>.Instance));
        }));
        using var admin = await Login(factory, "admin@kutax.test");
        var id = (await (await admin.PostAsJsonAsync("/api/rule-packs/draft", new { financialYearStart = 2026 }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var worker = new RulePackWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RulePackWorker>.Instance);
        Assert.True(await worker.DraftOneAsync(Ct));
        var waiting = await admin.GetFromJsonAsync<JsonElement>($"/api/rule-packs/{id}", Ct);
        Assert.Equal(("Queued", "RateLimited"), (waiting.GetProperty("status").GetString(), waiting.GetProperty("errorCode").GetString()));
        Assert.True(waiting.GetProperty("retryAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.False(await worker.DraftOneAsync(Ct)); // not before the provider's retry time
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
            var pack = await db.RulePacks.SingleAsync(p => p.Id == id, Ct);
            Assert.Equal(0, pack.Attempts);
            pack.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
        }
        Assert.True(await worker.DraftOneAsync(Ct));
        Assert.Equal("Draft", (await admin.GetFromJsonAsync<JsonElement>($"/api/rule-packs/{id}", Ct)).GetProperty("status").GetString());
        Assert.Equal(2 * TaxRuleCatalog.BuiltInPack(2025).Rules.Length + 1, model.Requests); // the 3 finished categories weren't asked again
    }

    [Fact]
    public async Task Packs_can_be_exported_edited_and_imported_and_a_new_version_supersedes_the_old()
    {
        using var factory = new ApiFactory();
        await factory.CreateAdministratorAsync("admin@kutax.test", Password);
        using var admin = await Login(factory, "admin@kutax.test");
        var exported = await admin.GetByteArrayAsync("/api/rule-packs/export/2025", Ct);
        var pack = JsonSerializer.Deserialize<RulePackDocument>(exported, RulePackDocument.Json)!;
        Assert.Empty(RuleEngine.Validate(pack));
        // A formula change arrives by import, with an example that matches it.
        var phone = pack.Rules.Single(r => r.Category == "PhoneInternet");
        var edited = pack with { Rules = [.. pack.Rules.Where(r => r != phone), phone with { Checks = [new("amount > maxBill", "NeedsReview", "Bills over ${maxBill} need a closer look.")],
            Parameters = new() { ["maxBill"] = 5000m }, Tests = [.. phone.Tests, new("Very large bill", new() { ["amount"] = 6000m, ["workUsePercent"] = 40m }, null, "NeedsReview", null)] }] };
        var imported = await admin.PostAsJsonAsync("/api/rule-packs/import", edited, RulePackDocument.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        var id = (await imported.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/rule-packs/{id}", Ct);
        Assert.Empty(detail.GetProperty("report").GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains(detail.GetProperty("report").GetProperty("fields").EnumerateArray(), f => f.GetProperty("field").GetString() == "checks" && f.GetProperty("changed").GetBoolean());

        // A broken import is kept for inspection but can't be activated.
        var broken = await admin.PostAsJsonAsync("/api/rule-packs/import", edited with { Rules = [phone with { Deductible = "amount * 2" }] }, RulePackDocument.Json, Ct);
        var brokenId = (await broken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/rule-packs/{brokenId}/activate", new { confirm = true, note = "x" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/rule-packs/{brokenId}/reject", new { confirm = true, note = "Broken" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/rule-packs/{id}/activate", new { confirm = true, note = "Synthetic import review" }, Ct)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        var rules = await db.LoadRulesAsync(Ct);
        Assert.Equal("AU-2025-PhoneInternet-v2", rules.Find(2025, "PhoneInternet")!.Version);
        Assert.False(rules.ByVersion("AU-2025-PhoneInternet-v1")!.IsActive); // kept for history and frozen packs
        Assert.Equal(1, rules.All.Count(r => r.IsActive && r.FinancialYearStart == 2025 && r.Category == "PhoneInternet"));
        var claim = new ExpenseAllocation { Category = "PhoneInternet", Amount = 6000m, WorkUsePercent = 40m, Basis = "b", EligibilityConfirmed = true };
        Assert.Equal("NeedsReview", TaxCalculator.Assess(new ReceiptTransaction { Date = new(2025, 9, 1), Amount = 6000m, Status = "Confirmed" }, claim, rules.Find(2025, "PhoneInternet"), true, true).Status);
    }

    [Fact]
    public void The_yearly_check_starts_when_that_years_instructions_are_due()
    {
        Assert.False(RulePackWorker.InCheckWindow(2026, new DateOnly(2027, 5, 31)));
        Assert.True(RulePackWorker.InCheckWindow(2026, new DateOnly(2027, 6, 1)));
        var pages = FakeAto.Pages;
        Assert.Contains("label E", RulePackDrafter.Pages(pages, TaxRuleCatalog.BuiltInPack(2025).Rules.Single(r => r.Category == "PhoneInternet")));
    }
}
