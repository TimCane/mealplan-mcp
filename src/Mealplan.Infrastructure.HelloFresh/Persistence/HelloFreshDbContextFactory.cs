using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mealplan.Infrastructure.HelloFresh.Persistence;

/// <summary>Design-time only, for `dotnet ef migrations`.</summary>
public class HelloFreshDbContextFactory : IDesignTimeDbContextFactory<HelloFreshDbContext>
{
    private const string DefaultConnection =
        "Host=db;Port=5432;Database=mealplanmcp;Username=mealplanmcp;Password=mealplanmcp";

    public HelloFreshDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MEALPLAN_CONNECTION") ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<HelloFreshDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", HelloFreshDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new HelloFreshDbContext(options);
    }
}
