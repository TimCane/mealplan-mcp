using Mealplan.Infrastructure.HelloFresh.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mealplan.IntegrationTests;

public class HelloFreshFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Tests share a container for speed but must not share rows.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();

        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                hellofresh.recipe_yield_ingredient,
                hellofresh.recipe_yield,
                hellofresh.recipe_step,
                hellofresh.recipe_nutrition,
                hellofresh.recipe_allergen,
                hellofresh.recipe_cuisine,
                hellofresh.recipe_tag,
                hellofresh.recipe_utensil,
                hellofresh.recipe,
                hellofresh.ingredient,
                hellofresh.allergen,
                hellofresh.cuisine,
                hellofresh.tag,
                hellofresh.utensil,
                hellofresh.category
            RESTART IDENTITY CASCADE
            """);
    }

    public HelloFreshDbContext CreateContext()
    {
        var connectionString = $"{_container.GetConnectionString()};Include Error Detail=true";

        var options = new DbContextOptionsBuilder<HelloFreshDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", HelloFreshDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new HelloFreshDbContext(options);
    }
}
