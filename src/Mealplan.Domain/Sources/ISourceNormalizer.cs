using Mealplan.Domain.Scraping;

namespace Mealplan.Domain.Sources;

/// <summary>
/// Turns one stored raw payload into that source's own normalised tables. Runs
/// as a separate job from crawling, so a mapping fix can be replayed over stored
/// payloads without touching the network.
/// </summary>
public interface ISourceNormalizer
{
    string Source { get; }

    /// <summary>
    /// Document types this normaliser handles. A crawler may store documents it
    /// only needs for change detection - Gousto list pages exist to discover
    /// slugs, not to be normalised.
    /// </summary>
    IReadOnlySet<DocumentType> Handles { get; }

    Task NormalizeAsync(ScrapeDocument document, CancellationToken ct = default);
}
