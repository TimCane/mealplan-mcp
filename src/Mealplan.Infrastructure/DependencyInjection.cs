using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Jobs;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Infrastructure.Reading;
using Mealplan.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the scrape store, the discovered sources and the jobs. Both
    /// hosts call this; only the scraper writes.
    /// </summary>
    public static IServiceCollection AddMealplanInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mealplan")
            ?? throw new InvalidOperationException(
                "Connection string 'Mealplan' is not configured.");

        services.AddDbContext<ScrapeDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", ScrapeDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        services.TryAddSingletonTimeProvider();
        services.AddScoped<IRawDocumentStore, RawDocumentStore>();
        services.AddScoped<IScrapeRunStore, ScrapeRunStore>();

        var sources = services.AddSourcesFromAssemblies(configuration);
        services.AddSourceOptions(configuration, sources);

        services.AddScoped<CrawlJob>();
        services.AddScoped<NormalizeJob>();
        services.AddScoped<RecipeQueryService>();
        services.AddSingleton<UnionViewBuilder>();

        // Collected after the source modules have run, so a source's context is
        // migrated without the host naming it.
        var contextTypes = services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => typeof(DbContext).IsAssignableFrom(type))
            .Distinct()
            .ToList();

        services.AddSingleton(new DatabaseMigrator(contextTypes));

        return services;
    }

    /// <summary>
    /// Binds Sources:{slug} per discovered source, so each can be tuned
    /// independently and an absent section still yields validated defaults.
    /// </summary>
    private static void AddSourceOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            services
                .AddOptions<SourceOptions>(source)
                .Bind(configuration.GetSection($"{SourceOptions.SectionName}:{source}"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(s => s.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
