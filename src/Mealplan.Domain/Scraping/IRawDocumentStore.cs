namespace Mealplan.Domain.Scraping;

/// <summary>
/// Persists raw payloads and reports whether anything actually changed, so a
/// re-crawl of unchanged recipes costs a hash comparison rather than a rewrite
/// and a normalise pass.
/// </summary>
public interface IRawDocumentStore
{
    Task<StoreOutcome> StoreAsync(RawDocument document, Guid runId, CancellationToken ct = default);

    /// <summary>Versions awaiting normalisation, oldest first.</summary>
    Task<IReadOnlyList<ScrapeDocument>> GetPendingNormalizationAsync(
        string source,
        int limit,
        CancellationToken ct = default);
}
