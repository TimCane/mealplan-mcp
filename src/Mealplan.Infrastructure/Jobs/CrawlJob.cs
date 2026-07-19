using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mealplan.Infrastructure.Jobs;

/// <summary>
/// Drives one crawl of one source: resume where the last run stopped, store what
/// the crawler yields, checkpoint the cursor as it goes, and record the outcome.
/// </summary>
public class CrawlJob(
    SourceRegistry registry,
    IRawDocumentStore documents,
    IScrapeRunStore runs,
    IOptionsMonitor<SourceOptions> options,
    ILogger<CrawlJob> logger)
{
    public async Task<ScrapeRun> RunAsync(
        string source,
        int? maxDocuments = null,
        CancellationToken ct = default)
    {
        var crawler = registry.Crawler(source);
        var settings = options.Get(source);

        var cursor = await ResumeCursorAsync(source, ct);
        var run = await runs.StartAsync(source, ct);

        logger.LogInformation(
            "Crawl of {Source} started as run {RunId}{Resumed}",
            source,
            run.Id,
            cursor is null ? string.Empty : ", resuming from a saved cursor");

        var fetched = 0;
        var changed = 0;
        var sinceCheckpoint = 0;
        string? latestCursor = cursor;

        try
        {
            var request = new CrawlRequest(cursor, maxDocuments);

            await foreach (var item in crawler.CrawlAsync(request, ct))
            {
                var outcome = await documents.StoreAsync(item.Document, run.Id, ct);

                fetched++;
                if (outcome is not StoreOutcome.Unchanged)
                {
                    changed++;
                }

                if (item.Cursor is not null)
                {
                    latestCursor = item.Cursor;
                }

                if (++sinceCheckpoint >= settings.CheckpointEvery && latestCursor is not null)
                {
                    await runs.SaveCursorAsync(run.Id, latestCursor, ct);
                    sinceCheckpoint = 0;
                }
            }

            if (latestCursor is not null)
            {
                await runs.SaveCursorAsync(run.Id, latestCursor, ct);
            }

            await runs.CompleteAsync(run.Id, ScrapeRunStatus.Succeeded, fetched, changed, error: null, ct);

            logger.LogInformation(
                "Crawl of {Source} finished: {Fetched} fetched, {Changed} changed",
                source,
                fetched,
                changed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The cursor is already saved, so the next run picks up here.
            await runs.CompleteAsync(
                run.Id, ScrapeRunStatus.Cancelled, fetched, changed, error: null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Crawl of {Source} failed after {Fetched} documents", source, fetched);
            await runs.CompleteAsync(
                run.Id, ScrapeRunStatus.Failed, fetched, changed, ex.Message, CancellationToken.None);
            throw;
        }

        run.DocumentsFetched = fetched;
        run.DocumentsChanged = changed;
        return run;
    }

    /// <summary>
    /// Only an interrupted run leaves work behind. Resuming after a successful
    /// one would skip everything it already covered.
    /// </summary>
    private async Task<string?> ResumeCursorAsync(string source, CancellationToken ct)
    {
        var latest = await runs.GetLatestAsync(source, ct);

        return latest?.Status is ScrapeRunStatus.Failed or ScrapeRunStatus.Cancelled
            ? latest.Cursor
            : null;
    }
}
