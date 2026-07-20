using System.Text.Json;
using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Gousto;
using Mealplan.Infrastructure.Gousto.Persistence;
using Mealplan.Infrastructure.HelloFresh;
using Mealplan.Infrastructure.HelloFresh.Persistence;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Infrastructure.Reading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Shares one loaded database across the test classes that read it. Any test
/// that mutates data belongs in its own fixture, not here.
/// </summary>
[CollectionDefinition(Name)]
public class CrossSourceCollection : ICollectionFixture<CrossSourceViewFixture>
{
    public const string Name = "cross-source views";
}

/// <summary>
/// One database holding both sources' schemas and the views over them, loaded
/// from the captured fixtures. This is the only place the two sources meet, so
/// it is where the union has to be proved.
/// </summary>
public class CrossSourceViewFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16").Build();

    public IReadOnlyList<ISourceSchema> Schemas { get; } =
        [new GoustoSchema(), new HelloFreshSchema()];

    /// <summary>For tests that host an app of their own against this database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using (var scrape = ScrapeContext())
        {
            // The scrape migrations install pg_trgm, which the typo-tolerant
            // search fallback needs.
            await scrape.Database.MigrateAsync();
        }

        await using (var gousto = GoustoContext())
        {
            await gousto.Database.MigrateAsync();
        }

        await using (var hellofresh = HelloFreshContext())
        {
            await hellofresh.Database.MigrateAsync();
        }

        await LoadFixturesAsync();
        await BuildViewsAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public RecipeQueryService CreateQueryService() =>
        new(ScrapeContext(), Schemas);

    private async Task LoadFixturesAsync()
    {
        await using var gousto = GoustoContext();
        var goustoNormalizer = new GoustoNormalizer(gousto, TimeProvider.System);

        foreach (var (slug, payload) in await GoustoFixturesAsync())
        {
            await goustoNormalizer.NormalizeAsync(Document("gousto", slug, payload));
        }

        await using var hellofresh = HelloFreshContext();
        var helloFreshNormalizer = new HelloFreshNormalizer(hellofresh, TimeProvider.System);

        foreach (var (key, payload) in await HelloFreshFixturesAsync())
        {
            await helloFreshNormalizer.NormalizeAsync(Document("hellofresh", key, payload));
        }
    }

    private static async Task<List<(string Key, string Payload)>> GoustoFixturesAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gousto");

        var files = new[]
        {
            ("open-steak-sandwich-balsamic-onions-chips", "recipe-open-steak-sandwich.json"),
            ("chicken-date-tamarind-curry", "recipe-chicken-date-tamarind-curry.json"),
        };

        var loaded = new List<(string, string)>();

        foreach (var (slug, file) in files)
        {
            loaded.Add((slug, await File.ReadAllTextAsync(Path.Combine(directory, file))));
        }

        return loaded;
    }

    private static async Task<List<(string Key, string Payload)>> HelloFreshFixturesAsync()
    {
        var page = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "hellofresh", "search-page.json"));

        using var document = JsonDocument.Parse(page);

        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => (item.GetProperty("id").GetString()!, item.GetRawText()))
            .ToList();
    }

    /// <summary>
    /// Uses the production view builder rather than assembling the SQL again
    /// here, so these tests cover the code that actually runs at startup.
    /// </summary>
    private async Task BuildViewsAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<ScrapeDbContext>(options => options
            .UseNpgsql($"{_container.GetConnectionString()};Include Error Detail=true")
            .UseSnakeCaseNamingConvention());

        foreach (var schema in Schemas)
        {
            services.AddSingleton(schema);
        }

        await using var provider = services.BuildServiceProvider();

        await new UnionViewBuilder(NullLogger<UnionViewBuilder>.Instance).BuildAsync(provider);
    }

    private static ScrapeDocument Document(string source, string key, string payload) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = source,
        DocumentType = DocumentType.Recipe,
        SourceKey = key,
        Version = 1,
        Payload = payload,
        ContentHash = [],
    };

    public ScrapeDbContext ScrapeContext() =>
        new(Options<ScrapeDbContext>(ScrapeDbContext.SchemaName));

    private GoustoDbContext GoustoContext() =>
        new(Options<GoustoDbContext>(GoustoDbContext.SchemaName));

    private HelloFreshDbContext HelloFreshContext() =>
        new(Options<HelloFreshDbContext>(HelloFreshDbContext.SchemaName));

    private DbContextOptions<T> Options<T>(string schema)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(
                $"{_container.GetConnectionString()};Include Error Detail=true",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .UseSnakeCaseNamingConvention()
            .Options;
}
