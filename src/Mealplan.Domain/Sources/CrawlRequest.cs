namespace Mealplan.Domain.Sources;

/// <summary>
/// What a crawl should do this time round.
/// </summary>
/// <param name="Cursor">
/// The resume point saved by the previous run, or null to start from the top.
/// Its shape is the crawler's own business - the scraper only stores it.
/// </param>
/// <param name="MaxDocuments">
/// Stop after this many documents. Null means crawl everything; a small number
/// is how a smoke test avoids a 485-request pass.
/// </param>
public sealed record CrawlRequest(string? Cursor = null, int? MaxDocuments = null);
