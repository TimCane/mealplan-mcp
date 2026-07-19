namespace Mealplan.Domain.Scraping;

/// <summary>
/// A stored raw payload. Rows are append-only per (source, type, key): a changed
/// payload becomes a new version rather than overwriting, so upstream edits stay
/// auditable and an old payload can be re-normalised.
/// </summary>
public class ScrapeDocument
{
    public Guid Id { get; set; }

    public required string Source { get; set; }

    public DocumentType DocumentType { get; set; }

    public required string SourceKey { get; set; }

    /// <summary>1 for the first payload seen, incrementing on every change.</summary>
    public int Version { get; set; }

    /// <summary>The response body, stored as jsonb.</summary>
    public required string Payload { get; set; }

    /// <summary>SHA-256 of the payload bytes. The change-detection key.</summary>
    public required byte[] ContentHash { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Moved on every fetch, including when nothing changed.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public Guid RunId { get; set; }

    /// <summary>Null until the normalise job has processed this version.</summary>
    public DateTimeOffset? NormalizedAt { get; set; }

    public string? NormalizeError { get; set; }
}
