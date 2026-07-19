using Mealplan.Domain.Scraping;

namespace Mealplan.Infrastructure.Jobs;

/// <summary>
/// Whether a source needs seeding when the scraper starts.
/// </summary>
public static class StartupCrawl
{
    /// <summary>
    /// Seed only when the source has never completed a crawl. The test is on
    /// runs rather than on whether any rows exist, so a redeploy against an
    /// existing database does not start unexpected traffic.
    /// </summary>
    /// <param name="latest">The most recent run for the source, or null.</param>
    public static bool ShouldSeed(ScrapeRun? latest) => latest?.Status switch
    {
        // Never crawled: a fresh deployment, which is the case this exists for.
        null => true,

        // Already has recipes. Leave it to the schedule.
        ScrapeRunStatus.Succeeded => false,

        // Interrupted before it ever succeeded. Resuming now is better than
        // waiting for the schedule, and the crawl picks up from its cursor.
        ScrapeRunStatus.Failed or ScrapeRunStatus.Cancelled => true,

        // A run is in flight, or the process died mid-crawl and left it marked
        // Running. Either way, enqueueing a second one would duplicate work.
        ScrapeRunStatus.Running => false,

        _ => false,
    };
}
