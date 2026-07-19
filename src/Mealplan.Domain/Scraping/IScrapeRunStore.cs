namespace Mealplan.Domain.Scraping;

public interface IScrapeRunStore
{
    Task<ScrapeRun> StartAsync(string source, CancellationToken ct = default);

    Task SaveCursorAsync(Guid runId, string cursor, CancellationToken ct = default);

    /// <summary>
    /// Records the outcome and what the run actually did. The counts are part of
    /// the outcome: a run that stored nothing but reports Succeeded is worse than
    /// one that fails, because nothing draws the eye to it.
    /// </summary>
    Task CompleteAsync(
        Guid runId,
        ScrapeRunStatus status,
        int documentsFetched,
        int documentsChanged,
        string? error = null,
        CancellationToken ct = default);

    /// <summary>The most recent run for a source, whatever its status.</summary>
    Task<ScrapeRun?> GetLatestAsync(string source, CancellationToken ct = default);

    /// <summary>
    /// Marks every run still flagged Running as Cancelled, returning how many.
    /// Called at startup, where the scraper is the only writer and no crawl has
    /// begun, so a Running row can only have been left by a process that died
    /// mid-crawl. Such a row otherwise blocks the source forever: seeding skips
    /// it as already in flight, and nothing else ever clears it.
    /// </summary>
    Task<int> CancelAbandonedAsync(CancellationToken ct = default);
}
