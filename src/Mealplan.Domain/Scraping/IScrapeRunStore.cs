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
}
