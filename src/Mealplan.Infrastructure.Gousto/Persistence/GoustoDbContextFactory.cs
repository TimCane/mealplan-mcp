using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mealplan.Infrastructure.Gousto.Persistence;

/// <summary>Design-time only, for `dotnet ef migrations`.</summary>
public class GoustoDbContextFactory : IDesignTimeDbContextFactory<GoustoDbContext>
{
    private const string DefaultConnection =
        "Host=db;Port=5432;Database=mealplanmcp;Username=mealplanmcp;Password=mealplanmcp";

    public GoustoDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MEALPLAN_CONNECTION") ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<GoustoDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", GoustoDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GoustoDbContext(options);
    }
}
