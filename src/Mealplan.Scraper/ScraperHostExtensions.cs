using Hangfire;
using Hangfire.PostgreSql;
using Mealplan.Domain.Scraping;
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

        // Two servers, one worker each, rather than one server with two workers:
        // a single server lets any worker take any queued job, so the second
        // would pick up a second crawl - the overlap the one-worker setting
        // exists to prevent.
        services.AddHangfireServer(options =>
        {
            options.ServerName = "crawl";

            // Crawls are paced deliberately and mostly idle. Concurrency here
            // would only let two sources overlap, which the VPN exit does not
            // need and the sites would not thank us for.
            options.WorkerCount = 1;

            // "default" stays listed so anything enqueued before the queues were
            // split still drains rather than sitting forever.
            options.Queues = [CrawlQueue, "default"];
        });

        services.AddHangfireServer(options =>
        {
            // Normalising is local work against stored payloads. On the crawl
            // queue it waited behind a crawl that runs for hours, so recipes
            // stayed unsearchable long after they had been fetched.
            options.ServerName = "normalize";
            options.WorkerCount = 1;
            options.Queues = [NormalizeQueue];
        });

        return services;
    }

    private const string CrawlQueue = "crawl";

    private const string NormalizeQueue = "normalize";

    /// <summary>
    /// Registers the recurring crawl for every discovered source, and seeds one
    /// immediately for any source that has never completed a crawl. A source with
    /// no Schedule, or Enabled false, is left to manual triggering.
    /// </summary>
    public static async Task ScheduleSourceCrawlsAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<SourceOptions>>();
        var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        var background = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        var runs = scope.ServiceProvider.GetRequiredService<IScrapeRunStore>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Mealplan.Scraper.Schedule");

        // Before any seeding decision: a run left Running by a killed process
        // reads as "a crawl is already in flight" and suppresses the seed.
        var abandoned = await runs.CancelAbandonedAsync(ct);
        if (abandoned > 0)
        {
            logger.LogWarning(
                "Cancelled {Count} run(s) a previous process left in flight",
                abandoned);
        }

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
                CrawlQueue,
                job => job.RunAsync(source, null, CancellationToken.None),
                settings.Schedule);

            // Normalising is cheap and idempotent, so it runs on its own clock
            // rather than being chained to the crawl. A failed crawl still gets
            // whatever it managed to store turned into recipes.
            recurring.AddOrUpdate<NormalizeJob>(
                normalizeJobId,
                NormalizeQueue,
                job => job.RunAsync(source, 200, CancellationToken.None),
                Cron.Hourly());

            logger.LogInformation(
                "Scheduled {Source}: crawl '{Schedule}', normalise hourly",
                source,
                settings.Schedule);

            if (!settings.CrawlOnStartup)
            {
                continue;
            }

            var latest = await runs.GetLatestAsync(source, ct);

            if (!StartupCrawl.ShouldSeed(latest))
            {
                continue;
            }

            // Queued rather than run inline: startup must not block on a crawl
            // that takes hours, and Hangfire owns the retry behaviour either way.
            background.Enqueue<CrawlJob>(
                CrawlQueue,
                job => job.RunAsync(source, null, CancellationToken.None));

            logger.LogInformation(
                "Queued a first crawl of {Source}; it has no completed run yet",
                source);
        }
    }
}
