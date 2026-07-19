using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the scrape store. Both hosts call this; only the scraper writes.
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

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(s => s.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
