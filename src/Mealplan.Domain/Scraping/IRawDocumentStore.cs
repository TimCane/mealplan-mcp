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

    /// <summary>
    /// Stamps a version as processed. A failure records the error and still
    /// stamps, so one bad payload cannot spin the queue forever; clearing
    /// normalized_at is how a fixed mapping gets replayed.
    /// </summary>
    Task MarkNormalizedAsync(Guid documentId, string? error = null, CancellationToken ct = default);
}
