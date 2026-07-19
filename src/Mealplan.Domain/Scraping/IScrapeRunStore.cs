namespace Mealplan.Domain.Scraping;

public interface IScrapeRunStore
{
    Task<ScrapeRun> StartAsync(string source, CancellationToken ct = default);

    Task SaveCursorAsync(Guid runId, string cursor, CancellationToken ct = default);

    Task CompleteAsync(Guid runId, ScrapeRunStatus status, string? error = null, CancellationToken ct = default);

    /// <summary>The most recent run for a source, whatever its status.</summary>
    Task<ScrapeRun?> GetLatestAsync(string source, CancellationToken ct = default);
}
