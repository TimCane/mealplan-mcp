namespace Mealplan.Domain.Scraping;

/// <summary>
/// A payload as it came off a source API, before any interpretation. Crawlers
/// yield these; the store decides whether it is new, changed or already known.
/// </summary>
/// <param name="Source">Source slug, e.g. "gousto".</param>
/// <param name="DocumentType">What kind of document this is.</param>
/// <param name="SourceKey">
/// The source's own stable identifier for the document - a slug or id. Together
/// with source and document type this identifies the thing across versions.
/// </param>
/// <param name="Payload">The raw JSON response body.</param>
public sealed record RawDocument(
    string Source,
    DocumentType DocumentType,
    string SourceKey,
    string Payload);
