using Mealplan.Domain.Scraping;

namespace Mealplan.Domain.Sources;

/// <summary>
/// One document off the wire, plus the resume point that reaches it. The crawler
/// reports the cursor rather than the scraper inferring it, because only the
/// crawler knows whether it is paging by offset, by slug queue, or by page token.
/// </summary>
/// <param name="Document">The payload to store.</param>
/// <param name="Cursor">
/// Resume point covering everything yielded so far. Saved periodically, so it
/// must be safe to restart from - re-fetching a few documents is fine, they hash
/// to Unchanged.
/// </param>
public sealed record CrawlItem(RawDocument Document, string? Cursor = null);
