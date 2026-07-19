using Hangfire.Common;
using Hangfire.Storage;
using Mealplan.Infrastructure.Jobs;

namespace Mealplan.Scraper;

/// <summary>
/// Whether Hangfire already holds a crawl of a source. An interrupted crawl
/// survives a restart in storage and is fetched again, so seeding next to it
/// would run the same crawl twice.
/// </summary>
public static class PendingCrawl
{
    private const int PageSize = 200;

    /// <summary>
    /// True when any enqueued, fetched, processing or scheduled job is a crawl
    /// of <paramref name="source"/>. Scheduled covers a failed crawl waiting on
    /// an automatic retry.
    /// </summary>
    public static bool ExistsFor(IMonitoringApi monitoring, string source)
    {
        var queued = monitoring.Queues().SelectMany(queue =>
            Pages((from, count) => monitoring.EnqueuedJobs(queue.Name, from, count)
                    .Select(x => x.Value?.Job))
                .Concat(Pages((from, count) => monitoring.FetchedJobs(queue.Name, from, count)
                    .Select(x => x.Value?.Job))));

        var active = Pages((from, count) => monitoring.ProcessingJobs(from, count)
            .Select(x => x.Value?.Job));

        var scheduled = Pages((from, count) => monitoring.ScheduledJobs(from, count)
            .Select(x => x.Value?.Job));

        return queued.Concat(active).Concat(scheduled)
            .Any(job => IsCrawlOf(job, source));
    }

    /// <summary>
    /// Whether a stored job is CrawlJob.RunAsync for this source. The job is
    /// null when storage holds an invocation that no longer deserialises.
    /// </summary>
    public static bool IsCrawlOf(Job? job, string source) =>
        job is not null
            && job.Type == typeof(CrawlJob)
            && job.Method.Name == nameof(CrawlJob.RunAsync)
            && job.Args.FirstOrDefault() is string first
            && string.Equals(first, source, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Job?> Pages(Func<int, int, IEnumerable<Job?>> page)
    {
        for (var from = 0; ; from += PageSize)
        {
            var batch = page(from, PageSize).ToList();

            foreach (var job in batch)
            {
                yield return job;
            }

            if (batch.Count < PageSize)
            {
                yield break;
            }
        }
    }
}
