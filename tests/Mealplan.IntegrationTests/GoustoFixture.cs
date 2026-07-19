using Mealplan.Infrastructure.Gousto.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mealplan.IntegrationTests;

public class GoustoFixture : IAsyncLifetime
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

    /// <summary>
    /// Empties the schema between tests. Tests share one container for speed,
    /// but sharing rows would make them order-dependent.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();

        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                gousto.recipe_yield_ingredient,
                gousto.recipe_yield,
                gousto.recipe_step,
                gousto.recipe_nutrition,
                gousto.pantry_item,
                gousto.recipe_allergen,
                gousto.recipe_category,
                gousto.recipe,
                gousto.ingredient,
                gousto.allergen,
                gousto.category,
                gousto.cuisine
            RESTART IDENTITY CASCADE
            """);
    }

    public GoustoDbContext CreateContext()
    {
        // Error detail makes constraint violations name the offending row, which
        // is the difference between a useful test failure and a guess.
        var connectionString = $"{_container.GetConnectionString()};Include Error Detail=true";

        var options = new DbContextOptionsBuilder<GoustoDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", GoustoDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GoustoDbContext(options);
    }
}
