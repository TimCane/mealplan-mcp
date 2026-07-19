using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Design-time only, for `dotnet ef migrations`. The connection is never opened
/// to scaffold a migration, so the local dev default is enough; override with
/// MEALPLAN_CONNECTION when pointing the tooling at another database.
/// </summary>
public class ScrapeDbContextFactory : IDesignTimeDbContextFactory<ScrapeDbContext>
{
    private const string DefaultConnection =
        "Host=db;Port=5432;Database=mealplanmcp;Username=mealplanmcp;Password=mealplanmcp";

    public ScrapeDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MEALPLAN_CONNECTION") ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<ScrapeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", ScrapeDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ScrapeDbContext(options);
    }
}
