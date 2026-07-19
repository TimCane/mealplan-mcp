using System.Net;
using FluentAssertions;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Gousto;
using Mealplan.Infrastructure.HelloFresh;
using Mealplan.Infrastructure.Sources;
using Mealplan.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mealplan.Tests;

/// <summary>
/// A source that starts refusing requests must fail the crawl, not end it. Both
/// crawlers used to stop enumerating, which CrawlJob records as a complete and
/// successful pass over however few documents it managed first - the run reads
/// green, the cursor is discarded, and the source is never retried.
/// </summary>
public class CrawlFailureTests
{
    private static readonly string ListPage = """
        {"data":{"count":1,"entries":[{"uid":"1","title":"One","url":"/curry/slug-one"}]}}
        """;

    private static readonly string EmptyListPage = """
        {"data":{"count":0,"entries":[]}}
        """;

    [Fact]
    public async Task Gousto_fails_the_crawl_when_the_list_endpoint_refuses()
    {
        var http = new FakeHttpHandler().RespondWith(HttpStatusCode.Forbidden);

        var act = async () => await Crawl(GoustoCrawler(http));

        await act.Should().ThrowAsync<SourceFetchException>()
            .Where(e => e.Path.Contains("recipes?"));
    }

    [Fact]
    public async Task Gousto_fails_the_crawl_when_a_recipe_detail_refuses()
    {
        // The shape of the live incident: one list page, then the source starts
        // turning requests away partway through the details it named.
        var http = new FakeHttpHandler()
            .Respond(ListPage)
            .RespondWith(HttpStatusCode.TooManyRequests);

        var act = async () => await Crawl(GoustoCrawler(http));

        await act.Should().ThrowAsync<SourceFetchException>()
            .Where(e => e.Path.Contains("slug-one"));
    }

    [Fact]
    public async Task Gousto_ends_cleanly_when_the_catalogue_runs_out()
    {
        var http = new FakeHttpHandler().Respond(EmptyListPage);

        var items = await Crawl(GoustoCrawler(http));

        items.Should().BeEmpty("an empty entry list is the end, not a failure");
    }

    [Fact]
    public async Task HelloFresh_fails_the_crawl_when_the_search_refuses()
    {
        var http = new FakeHttpHandler().RespondWith(HttpStatusCode.Unauthorized);

        var act = async () => await Crawl(HelloFreshCrawler(http));

        await act.Should().ThrowAsync<SourceFetchException>()
            .Where(e => e.Path.Contains("search"));
    }

    [Fact]
    public async Task HelloFresh_fails_the_crawl_when_a_later_page_refuses()
    {
        var http = new FakeHttpHandler()
            .Respond("""{"total":100,"items":[{"id":"a","slug":"one"}]}""")
            .RespondWith(HttpStatusCode.Forbidden);

        var act = async () => await Crawl(HelloFreshCrawler(http));

        await act.Should().ThrowAsync<SourceFetchException>();
    }

    [Fact]
    public async Task HelloFresh_ends_cleanly_when_the_catalogue_is_covered()
    {
        var http = new FakeHttpHandler()
            .Respond("""{"total":1,"items":[{"id":"a","slug":"one"}]}""");

        var items = await Crawl(HelloFreshCrawler(http));

        items.Should().HaveCount(1);
    }

    private static async Task<List<CrawlItem>> Crawl(ISourceCrawler crawler)
    {
        var items = new List<CrawlItem>();

        await foreach (var item in crawler.CrawlAsync(new CrawlRequest(null, null)))
        {
            items.Add(item);
        }

        return items;
    }

    private static GoustoCrawler GoustoCrawler(FakeHttpHandler http) =>
        new(new FakeHttpClientFactory(http, "https://gousto.test/"),
            new StaticOptionsMonitor<SourceOptions>(new SourceOptions()),
            NullLogger<GoustoCrawler>.Instance);

    private static HelloFreshCrawler HelloFreshCrawler(FakeHttpHandler http) =>
        new(new FakeHttpClientFactory(http, "https://hellofresh.test/"),
            new StaticOptionsMonitor<SourceOptions>(new SourceOptions()),
            new StaticOptionsMonitor<HelloFreshOptions>(new HelloFreshOptions()),
            NullLogger<HelloFreshCrawler>.Instance);
}
