using System.Net;

namespace Mealplan.Domain.Sources;

/// <summary>
/// A source failed a request that the retry pipeline had already given up on.
/// Crawlers throw this rather than ending their enumeration, so the run records
/// Failed and keeps its cursor - a crawl that stopped early and one that reached
/// the end of the catalogue are otherwise identical from the outside.
/// </summary>
public class SourceFetchException(string source, string path, string reason)
    : Exception($"{source} GET {path} failed: {reason}")
{
    public SourceFetchException(string source, string path, HttpStatusCode status)
        : this(source, path, $"HTTP {(int)status}")
    {
    }

    /// <summary>Named to stay clear of <see cref="Exception.Source"/>.</summary>
    public string SourceSlug { get; } = source;

    public string Path { get; } = path;
}
