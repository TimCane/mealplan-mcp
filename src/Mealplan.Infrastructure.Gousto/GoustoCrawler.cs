using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Gousto.Api;
using Mealplan.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mealplan.Infrastructure.Gousto;

/// <summary>
/// Two phases, because Gousto's list pages are thin: page the list to discover
/// slugs, then fetch each detail. List pages are stored too - they are cheap and
/// let a later run notice a recipe changed without fetching every detail.
/// </summary>
public class GoustoCrawler(
    IHttpClientFactory clients,
    IOptionsMonitor<SourceOptions> options,
    ILogger<GoustoCrawler> logger) : ISourceCrawler
{
    public string Source => GoustoSchema.SourceSlug;

    public async IAsyncEnumerable<CrawlItem> CrawlAsync(
        CrawlRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = clients.CreateClient(Source);
        var settings = options.Get(Source);
        var cursor = GoustoCursor.Parse(request.Cursor);

        var yielded = 0;
        var offset = cursor.Offset;
        var pending = new Queue<string>(cursor.PendingSlugs);

        while (!ct.IsCancellationRequested)
        {
            // Finish the slugs already discovered before asking for more, so a
            // resumed crawl does not re-page ground it has already covered.
            while (pending.Count > 0)
            {
                var slug = pending.Dequeue();

                var detail = await GetAsync(client, $"recipe/{slug}", ct);
                if (detail is null)
                {
                    logger.LogWarning("Gousto recipe {Slug} could not be fetched, skipping", slug);
                }
                else
                {
                    yield return new CrawlItem(
                        new RawDocument(Source, DocumentType.Recipe, slug, detail),
                        new GoustoCursor(offset, pending).ToJson());

                    if (++yielded >= (request.MaxDocuments ?? int.MaxValue))
                    {
                        yield break;
                    }
                }
            }

            var pageJson = await GetAsync(
                client,
                $"recipes?category=recipes&limit={settings.PageSize}&offset={offset}",
                ct);

            if (pageJson is null)
            {
                yield break;
            }

            var page = Deserialize<GoustoListResponse>(pageJson);
            var entries = page?.Data?.Entries ?? [];

            if (entries.Count == 0)
            {
                logger.LogInformation("Gousto list exhausted at offset {Offset}", offset);
                yield break;
            }

            foreach (var slug in entries.Select(Slug).Where(s => s is not null))
            {
                pending.Enqueue(slug!);
            }

            offset += settings.PageSize;

            yield return new CrawlItem(
                new RawDocument(
                    Source,
                    DocumentType.RecipeSummary,
                    $"recipes:offset={offset - settings.PageSize}",
                    pageJson),
                new GoustoCursor(offset, pending).ToJson());

            if (++yielded >= (request.MaxDocuments ?? int.MaxValue))
            {
                yield break;
            }
        }
    }

    /// <summary>The detail endpoint keys on the slug, which only appears in the URL.</summary>
    internal static string? Slug(GoustoListEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Url) ? null : entry.Url.Trim('/');

    private static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<string?> GetAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Gousto GET {Path} returned {Status}",
                path,
                (int)response.StatusCode);

            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        // The API answers 204 with an empty body for some repeated requests.
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}

/// <summary>
/// Where a crawl got to: the next list offset, plus slugs discovered but not yet
/// fetched. Restarting from it re-fetches at most one page of details, and those
/// hash to Unchanged.
/// </summary>
internal sealed record GoustoCursor(int Offset, IReadOnlyCollection<string> PendingSlugs)
{
    public static GoustoCursor Empty { get; } = new(0, []);

    public static GoustoCursor Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<GoustoCursor>(json) ?? Empty;
        }
        catch (JsonException)
        {
            // A cursor written by an older version. Starting over is correct:
            // unchanged recipes cost a hash comparison each.
            return Empty;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
