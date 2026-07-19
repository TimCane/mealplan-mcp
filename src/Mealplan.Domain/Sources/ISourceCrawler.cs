using Mealplan.Domain.Scraping;

namespace Mealplan.Domain.Sources;

/// <summary>
/// Fetches raw documents from one source. Paging, authentication, rate limiting
/// and retry policy are the crawler's own business - sources differ too much for
/// a shared template. HelloFresh returns complete recipes from one paged search;
/// Gousto needs a list pass to discover slugs and then a detail call each.
/// </summary>
public interface ISourceCrawler
{
    /// <summary>Source slug. Must match the normaliser and schema for this source.</summary>
    string Source { get; }

    /// <summary>
    /// Yields documents lazily so the scraper can store, count and checkpoint as
    /// they arrive rather than buffering a whole crawl in memory.
    /// </summary>
    IAsyncEnumerable<CrawlItem> CrawlAsync(CrawlRequest request, CancellationToken ct = default);
}
