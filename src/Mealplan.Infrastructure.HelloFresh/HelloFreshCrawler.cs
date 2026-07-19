using System.Runtime.CompilerServices;
using System.Text.Json;
using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.HelloFresh.Api;
using Mealplan.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mealplan.Infrastructure.HelloFresh;

/// <summary>
/// One paginated pass. The search endpoint returns complete recipes, so unlike
/// Gousto there is no detail call to follow - each page yields its recipes
/// individually, which keeps change detection per recipe rather than per page.
/// </summary>
public class HelloFreshCrawler(
    IHttpClientFactory clients,
    IOptionsMonitor<SourceOptions> options,
    ILogger<HelloFreshCrawler> logger) : ISourceCrawler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Source => HelloFreshSchema.SourceSlug;

    public async IAsyncEnumerable<CrawlItem> CrawlAsync(
        CrawlRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = clients.CreateClient(Source);
        var settings = options.Get(Source);

        var skip = HelloFreshCursor.Parse(request.Cursor).Skip;
        var yielded = 0;

        while (!ct.IsCancellationRequested)
        {
            var url = $"search?country=GB&locale=en-GB&skip={skip}&take={settings.PageSize}";

            using var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "HelloFresh search at skip {Skip} returned {Status}",
                    skip,
                    (int)response.StatusCode);

                yield break;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var page = JsonSerializer.Deserialize<HelloFreshSearchResponse>(body, JsonOptions);
            var items = page?.Items ?? [];

            if (items.Count == 0)
            {
                logger.LogInformation(
                    "HelloFresh search exhausted at skip {Skip} of {Total}",
                    skip,
                    page?.Total ?? 0);

                yield break;
            }

            skip += items.Count;
            var cursor = new HelloFreshCursor(skip).ToJson();

            foreach (var item in items)
            {
                var key = item.Id ?? item.Slug;
                if (key is null)
                {
                    continue;
                }

                // Each recipe is stored on its own, so an unchanged recipe on a
                // page where a sibling changed still hashes to Unchanged.
                yield return new CrawlItem(
                    new RawDocument(
                        Source,
                        DocumentType.Recipe,
                        key,
                        JsonSerializer.Serialize(item, JsonOptions)),
                    cursor);

                if (++yielded >= (request.MaxDocuments ?? int.MaxValue))
                {
                    yield break;
                }
            }

            if (page is not null && skip >= page.Total)
            {
                logger.LogInformation("HelloFresh crawl covered all {Total} recipes", page.Total);
                yield break;
            }
        }
    }
}

/// <summary>How far through the search a crawl got.</summary>
internal sealed record HelloFreshCursor(int Skip)
{
    public static HelloFreshCursor Empty { get; } = new(0);

    public static HelloFreshCursor Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<HelloFreshCursor>(json) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
