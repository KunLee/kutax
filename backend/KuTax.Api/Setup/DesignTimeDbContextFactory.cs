using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KuTax.Api.Setup;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KuTaxDbContext>
{
    public KuTaxDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<KuTaxDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database")
                ?? "Host=localhost;Database=kutax")
            .Options);
}
