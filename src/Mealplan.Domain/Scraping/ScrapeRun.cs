namespace Mealplan.Domain.Scraping;

/// <summary>
/// One crawl of one source. <see cref="Cursor"/> holds whatever the crawler needs
/// to pick up where it stopped, so an interrupted run resumes rather than
/// restarting a few hundred requests from the top.
/// </summary>
public class ScrapeRun
{
    public Guid Id { get; set; }

    public required string Source { get; set; }

    public ScrapeRunStatus Status { get; set; } = ScrapeRunStatus.Running;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int DocumentsFetched { get; set; }

    public int DocumentsChanged { get; set; }

    /// <summary>Crawler-defined resume point, as JSON.</summary>
    public string? Cursor { get; set; }

    public string? Error { get; set; }
}
