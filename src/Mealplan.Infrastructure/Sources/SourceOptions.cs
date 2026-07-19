using System.ComponentModel.DataAnnotations;

namespace Mealplan.Infrastructure.Sources;

/// <summary>
/// Politeness and scheduling, per source. Bound from Sources:{slug}; a source
/// with no entry gets these defaults, which are deliberately slow.
/// </summary>
public class SourceOptions
{
    public const string SectionName = "Sources";

    /// <summary>Pause between requests, before jitter.</summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Fraction of the delay applied as random jitter, so a crawl does not
    /// arrive on a metronome. 0.5 means the delay varies by plus or minus half.
    /// </summary>
    [Range(0, 1)]
    public double RequestJitter { get; set; } = 0.5;

    /// <summary>Requests in flight per source. One keeps crawls unobtrusive.</summary>
    [Range(1, 16)]
    public int MaxConcurrency { get; set; } = 1;

    [Range(1, 200)]
    public int PageSize { get; set; } = 16;

    /// <summary>Cron for the recurring crawl. Null disables scheduling.</summary>
    public string? Schedule { get; set; } = "0 3 * * 0";

    [Range(0, 20)]
    public int MaxRetries { get; set; } = 5;

    /// <summary>Ceiling on exponential backoff between retries.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Documents stored between cursor checkpoints.</summary>
    [Range(1, 1000)]
    public int CheckpointEvery { get; set; } = 25;

    /// <summary>
    /// Sent on every request. A real browser string is not evasion here - these
    /// endpoints reject the default .NET agent outright.
    /// </summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0";

    /// <summary>Set false to leave a source out of the recurring schedule.</summary>
    public bool Enabled { get; set; } = true;
}
