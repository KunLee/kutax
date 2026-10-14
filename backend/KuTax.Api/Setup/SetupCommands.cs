using KuTax.Core.FinancialYears;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Setup;

public static class SetupCommands
{
    public static async Task<bool> RunAsync(WebApplication app, string[] args)
    {
        var commands = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (!commands.Any(a => a is "--migrate" or "--seed-development" or "--initialise-storage" or "--bootstrap-admin" or "--verify-recovery" or "--generate-evaluation-fixtures" or "--evaluate-synthetic")) return false;
        if (commands.Any(a => a is not ("--migrate" or "--seed-development" or "--initialise-storage" or "--bootstrap-admin" or "--verify-recovery" or "--generate-evaluation-fixtures" or "--evaluate-synthetic")))
            throw new InvalidOperationException("Unknown setup command.");
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        if(commands.Contains("--generate-evaluation-fixtures")||commands.Contains("--evaluate-synthetic"))await ExtractionEvaluation.RunAsync(services,app.Configuration,commands.Contains("--evaluate-synthetic"),CancellationToken.None);
        if(commands.Contains("--verify-recovery"))await RecoveryVerification.VerifyAsync(services.GetRequiredService<KuTaxDbContext>(),services.GetRequiredService<KuTax.Core.Receipts.IDocumentStorage>(),CancellationToken.None);
        if (commands.Contains("--seed-development") && !app.Environment.IsDevelopment())
            throw new InvalidOperationException("Synthetic account seeding is restricted to Development.");
        if (commands.Contains("--migrate"))
            await services.GetRequiredService<KuTaxDbContext>().Database.MigrateAsync();
        if (commands.Contains("--initialise-storage"))
            await services.GetRequiredService<StorageSetup>().InitialiseAsync();
        if (commands.Contains("--seed-development"))
        {
            var password = app.Configuration["DevelopmentSeed:Password"]
                ?? throw new InvalidOperationException("Set DevelopmentSeed:Password in user secrets or environment.");
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            const string email = "demo@kutax.test";
            if (await users.FindByEmailAsync(email) is null)
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Australia/Perth").DateTime);
                var result = await users.CreateAsync(new ApplicationUser
                {
                    UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Development owner",
                    SelectedFinancialYearStart = FinancialYear.FromDate(today).StartYear
                }, password);
                if (!result.Succeeded)
                    throw new InvalidOperationException("Seed failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
            }
            await GrantAdministratorAsync(services, (await users.FindByEmailAsync(email))!);
        }
        if (commands.Contains("--bootstrap-admin"))
        {
            var email = app.Configuration["BootstrapAdmin:Email"];
            var password = app.Configuration["BootstrapAdmin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Configure BootstrapAdmin:Email and BootstrapAdmin:Password through protected configuration.");
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email.Trim());
            if (user is null)
            {
                user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim(), MustChangePassword = true };
                var result = await users.CreateAsync(user, password);
                if (!result.Succeeded) throw new InvalidOperationException("Administrator creation failed: " + string.Join(", ", result.Errors.Select(error => error.Code)));
            }
            else if (!user.IsEnabled || !await users.CheckPasswordAsync(user, password))
                throw new InvalidOperationException("Existing administrator bootstrap requires the account's current password and an enabled account.");
            await GrantAdministratorAsync(services, user);
        }
        return true;
    }

    private static async Task GrantAdministratorAsync(IServiceProvider services, ApplicationUser user)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(ApplicationRoles.Administrator))
        {
            var created = await roles.CreateAsync(new IdentityRole(ApplicationRoles.Administrator));
            if (!created.Succeeded) throw new InvalidOperationException("Unable to provision administrator role.");
        }
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (!await users.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            var granted = await users.AddToRoleAsync(user, ApplicationRoles.Administrator);
            if (!granted.Succeeded) throw new InvalidOperationException("Unable to grant administrator role.");
            var revoked = await users.UpdateSecurityStampAsync(user);
            if (!revoked.Succeeded) throw new InvalidOperationException("Unable to refresh administrator sessions.");
        }
    }
}

