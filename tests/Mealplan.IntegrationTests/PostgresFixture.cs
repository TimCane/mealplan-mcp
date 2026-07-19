using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mealplan.IntegrationTests;

/// <summary>
/// A real Postgres per test class. The in-memory provider would not exercise
/// jsonb, partial indexes or the snake_case mapping, which is most of what these
/// tests are checking.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public ScrapeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ScrapeDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", ScrapeDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ScrapeDbContext(options);
    }
}
