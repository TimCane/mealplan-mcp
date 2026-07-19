using Mealplan.Domain.Sources;

namespace Mealplan.Infrastructure.Sources;

/// <summary>
/// Looks up a source's parts by slug. Fails loudly on a half-registered source:
/// a crawler with no normaliser would scrape happily and produce nothing, which
/// is worse than not starting.
/// </summary>
public class SourceRegistry
{
    private readonly Dictionary<string, ISourceCrawler> _crawlers;
    private readonly Dictionary<string, ISourceNormalizer> _normalizers;
    private readonly Dictionary<string, ISourceSchema> _schemas;

    public SourceRegistry(
        IEnumerable<ISourceCrawler> crawlers,
        IEnumerable<ISourceNormalizer> normalizers,
        IEnumerable<ISourceSchema> schemas)
    {
        _crawlers = crawlers.ToDictionary(c => c.Source, StringComparer.OrdinalIgnoreCase);
        _normalizers = normalizers.ToDictionary(n => n.Source, StringComparer.OrdinalIgnoreCase);
        _schemas = schemas.ToDictionary(s => s.Source, StringComparer.OrdinalIgnoreCase);

        var incomplete = _crawlers.Keys
            .Union(_normalizers.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(_schemas.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(slug => !_crawlers.ContainsKey(slug)
                || !_normalizers.ContainsKey(slug)
                || !_schemas.ContainsKey(slug))
            .ToList();

        if (incomplete.Count > 0)
        {
            throw new InvalidOperationException(
                $"Incomplete source registration for: {string.Join(", ", incomplete)}. "
                + "Each source needs a crawler, a normaliser and a schema.");
        }
    }

    public IReadOnlyCollection<string> Sources => _schemas.Keys;

    public IReadOnlyCollection<ISourceSchema> Schemas => _schemas.Values;

    public ISourceCrawler Crawler(string source) => Get(_crawlers, source, "crawler");

    public ISourceNormalizer Normalizer(string source) => Get(_normalizers, source, "normaliser");

    public ISourceSchema Schema(string source) => Get(_schemas, source, "schema");

    private static T Get<T>(Dictionary<string, T> map, string source, string what) =>
        map.TryGetValue(source, out var value)
            ? value
            : throw new KeyNotFoundException($"No {what} registered for source '{source}'.");
}
