using System.Threading.RateLimiting;
using System.Net;
using KuTax.Api.Features.Accounts;
using KuTax.Api.Features.Ai;
using KuTax.Api.Features.Authentication;
using KuTax.Api.Features.Users;
using KuTax.Api.Features.Receipts;
using KuTax.Api.Setup;
using KuTax.Infrastructure;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Storage;
using KuTax.Infrastructure.Processing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var setupSwitches = new[] { "--migrate", "--seed-development", "--initialise-storage", "--bootstrap-admin", "--verify-recovery", "--generate-evaluation-fixtures", "--evaluate-synthetic" };
var builder = WebApplication.CreateBuilder(args.Except(setupSwitches).ToArray());
if(builder.Environment.IsProduction())ProductionConfiguration.Validate(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Configuration.GetValue<bool>("Ai:SyntheticFixtures") && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Synthetic extraction fixtures are only allowed in Development.");
if (builder.Configuration.GetValue("Processing:Enabled",true)) builder.Services.AddHostedService<ReceiptWorker>();
if (builder.Configuration.GetValue("Processing:Enabled",true)) builder.Services.AddHostedService<KuTax.Infrastructure.Reports.ExportWorker>();
builder.Services.AddSingleton<KuTax.Infrastructure.Rules.RuleCheckRequests>();
builder.Services.AddSingleton<KuTax.Infrastructure.Email.IEmailSender, KuTax.Infrastructure.Email.SmtpEmailSender>();
if (builder.Configuration.GetValue("Processing:Enabled",true)) builder.Services.AddHostedService<KuTax.Infrastructure.Rules.RulePackWorker>();
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Managed hosting (Cloud Run): the container is reachable only through Google's front end, which terminates TLS
    // and sets X-Forwarded-Proto/For from varying addresses, so it is trusted for the last hop only (ForwardLimit 1:
    // the client address Google appends, not anything a client sent). Self-hosted: loopback plus configured proxy IPs.
    if (builder.Configuration.GetValue<bool>("Production:ManagedServices"))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
    }
    foreach (var address in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(address));
});
builder.Services.AddAuthorization();
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<KuTaxDbContext>().AddDefaultTokenProviders();
// Password resets and disabling an account revoke existing cookies on their next request.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "KuTax.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "KuTax.Antiforgery";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    // Self-registration is limited per client address so one source cannot mass-create accounts.
    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
});
var protection = builder.Services.AddDataProtection().SetApplicationName("KuTax");
// Keys live on a persistent disk (VPS, local) or, for hosts whose disk is replaced on every deploy (Cloud Run), in the
// database, encrypted with a certificate held in the host's secret store.
if (builder.Configuration["DataProtection:Store"] == "Database")
    protection.PersistKeysToDbContext<KuTax.Infrastructure.Persistence.KuTaxDbContext>();
else if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
if (builder.Configuration["DataProtection:CertificateBase64"] is { Length: > 0 } certificateText)
{
    var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificateText),
        builder.Configuration["DataProtection:CertificatePassword"], System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
    protection.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
}

var app = builder.Build();
if (await SetupCommands.RunAsync(app, args)) return;

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.Use(async(context,next)=>
{
    if(!app.Environment.IsDevelopment()&&!context.Request.IsHttps&&context.Request.Path!="/api/health/live")
    {await Results.Problem("HTTPS is required.",statusCode:400).ExecuteAsync(context);return;}
    await next(context);
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/api"))
    {
        var users = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(context.User);
        if (user is null || !user.IsEnabled)
        {
            await context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
            context.Response.StatusCode = 401;
            return;
        }
        if (user.MustChangePassword && context.Request.Path.Value is not
            ("/api/auth/csrf" or "/api/auth/session" or "/api/auth/logout" or "/api/accounts/me/password"))
        {
            await Results.Problem("Change your temporary password to continue.", statusCode: 403,
                extensions: new Dictionary<string, object?> { ["code"] = "PasswordChangeRequired" }).ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
// Acting for someone who shared their tax records: checked here, before any endpoint reads data.
app.Use((context, next) => KuTax.Api.Features.Sharing.Taxpayer.ResolveAsync(context, () => next(context)));
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem("Refresh the page and try again.", statusCode: 400).ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health/ready", async (KuTaxDbContext db, StorageSetup storage, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return Results.StatusCode(503);
        await storage.CheckAsync(ct);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        // Avoid returning connection details, credentials or storage errors to the caller.
        return Results.StatusCode(503);
    }
});
app.MapAuthentication(app.Environment.IsDevelopment());
app.MapAccounts();
app.MapUsers();
app.MapAiSettings();
app.MapReceipts();
app.MapReview();
app.MapAllocations();
KuTax.Api.Features.Claims.ClaimEndpoints.MapClaims(app);
KuTax.Api.Features.Claims.QuickReviewEndpoints.MapQuickReview(app);
KuTax.Api.Features.Claims.RulePackEndpoints.MapRulePacks(app);
KuTax.Api.Features.Claims.ExportEndpoints.MapExports(app);
KuTax.Api.Features.Sharing.SharingEndpoints.MapSharing(app);
KuTax.Api.Features.Reports.ReportEndpoints.MapReports(app);
// An unknown API route must never return the SPA HTML with a success status.
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;

