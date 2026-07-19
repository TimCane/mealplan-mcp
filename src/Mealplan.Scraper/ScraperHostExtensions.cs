using Hangfire;
using Hangfire.PostgreSql;
using Mealplan.Infrastructure.Jobs;
using Mealplan.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace Mealplan.Scraper;

public static class ScraperHostExtensions
{
    /// <summary>
    /// Hangfire keeps job state in the same Postgres as the scrape data, so a
    /// restart mid-crawl loses nothing and retries survive the process.
    /// </summary>
    public static IServiceCollection AddScraperJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mealplan")
            ?? throw new InvalidOperationException(
                "Connection string 'Mealplan' is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(postgres => postgres.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer(options =>
        {
            // Crawls are paced deliberately and mostly idle. Concurrency here
            // would only let two sources overlap, which the VPN exit does not
            // need and the sites would not thank us for.
            options.WorkerCount = 1;
            options.Queues = ["default"];
        });

        return services;
    }

    /// <summary>
    /// Registers the recurring crawl for every discovered source. A source with
    /// no Schedule, or Enabled false, is left to manual triggering.
    /// </summary>
    public static void ScheduleSourceCrawls(this IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<SourceOptions>>();
        var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Mealplan.Scraper.Schedule");

        foreach (var source in registry.Sources)
        {
            var settings = options.Get(source);
            var crawlJobId = $"crawl:{source}";
            var normalizeJobId = $"normalize:{source}";

            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Schedule))
            {
                recurring.RemoveIfExists(crawlJobId);
                recurring.RemoveIfExists(normalizeJobId);
                logger.LogInformation("Source {Source} is not scheduled", source);
                continue;
            }

            recurring.AddOrUpdate<CrawlJob>(
                crawlJobId,
                job => job.RunAsync(source, null, CancellationToken.None),
                settings.Schedule);

            // Normalising is cheap and idempotent, so it runs on its own clock
            // rather than being chained to the crawl. A failed crawl still gets
            // whatever it managed to store turned into recipes.
            recurring.AddOrUpdate<NormalizeJob>(
                normalizeJobId,
                job => job.RunAsync(source, 200, CancellationToken.None),
                Cron.Hourly());

            logger.LogInformation(
                "Scheduled {Source}: crawl '{Schedule}', normalise hourly",
                source,
                settings.Schedule);
        }
    }
}
