using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Jobs;
using Mealplan.Infrastructure.Sources;
using Mealplan.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mealplan.Tests;

public class CrawlJobTests
{
    private readonly InMemoryDocumentStore _documents = new();
    private readonly InMemoryRunStore _runs = new();

    [Fact]
    public async Task Every_yielded_document_is_stored_and_the_run_succeeds()
    {
        var job = CreateJob(Items(3));

        var run = await job.RunAsync("fake");

        _documents.Documents.Should().HaveCount(3);
        _runs.Runs.Single().Status.Should().Be(ScrapeRunStatus.Succeeded);
        run.DocumentsFetched.Should().Be(3);
    }

    [Fact]
    public async Task Only_changed_documents_count_as_changed()
    {
        _documents.Outcome = document =>
            document.SourceKey == "key-1" ? StoreOutcome.Unchanged : StoreOutcome.Versioned;

        var job = CreateJob(Items(3));

        var run = await job.RunAsync("fake");

        run.DocumentsFetched.Should().Be(3);
        run.DocumentsChanged.Should().Be(2, "an unchanged payload is not a change");
    }

    [Fact]
    public async Task The_cursor_is_checkpointed_during_the_crawl_and_saved_at_the_end()
    {
        var job = CreateJob(Items(5), o => o.CheckpointEvery = 2);

        await job.RunAsync("fake");

        _runs.SavedCursors.Should().NotBeEmpty();
        _runs.Runs.Single().Cursor.Should().Be("cursor-4", "the last cursor yielded wins");
    }

    [Fact]
    public async Task A_failed_crawl_is_recorded_and_rethrown_with_its_cursor_intact()
    {
        var crawler = new FakeCrawler("fake", Items(3)) { ThrowAfterYielding = new HttpRequestException("429") };
        var job = CreateJob(crawler, o => o.CheckpointEvery = 1);

        var act = async () => await job.RunAsync("fake");

        await act.Should().ThrowAsync<HttpRequestException>();

        var run = _runs.Runs.Single();
        run.Status.Should().Be(ScrapeRunStatus.Failed);
        run.Error.Should().Contain("429");
        run.Cursor.Should().Be("cursor-2", "progress up to the failure must survive");
    }

    [Fact]
    public async Task A_failed_run_is_resumed_from_its_cursor()
    {
        _runs.Seed(new ScrapeRun
        {
            Id = Guid.CreateVersion7(),
            Source = "fake",
            Status = ScrapeRunStatus.Failed,
            StartedAt = DateTimeOffset.UnixEpoch,
            Cursor = "cursor-7",
        });

        var crawler = new FakeCrawler("fake", Items(1));
        var job = CreateJob(crawler);

        await job.RunAsync("fake");

        crawler.LastRequest!.Cursor.Should().Be("cursor-7");
    }

    [Fact]
    public async Task A_successful_run_is_not_resumed()
    {
        _runs.Seed(new ScrapeRun
        {
            Id = Guid.CreateVersion7(),
            Source = "fake",
            Status = ScrapeRunStatus.Succeeded,
            StartedAt = DateTimeOffset.UnixEpoch,
            Cursor = "cursor-7",
        });

        var crawler = new FakeCrawler("fake", Items(1));
        var job = CreateJob(crawler);

        await job.RunAsync("fake");

        crawler.LastRequest!.Cursor.Should().BeNull(
            "resuming after success would skip everything that run covered");
    }

    private static IReadOnlyList<CrawlItem> Items(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new CrawlItem(
                new RawDocument("fake", DocumentType.Recipe, $"key-{i}", $$"""{"i":{{i}}}"""),
                $"cursor-{i}"))
            .ToList();

    private CrawlJob CreateJob(
        IReadOnlyList<CrawlItem> items,
        Action<SourceOptions>? configure = null) =>
        CreateJob(new FakeCrawler("fake", items), configure);

    private CrawlJob CreateJob(FakeCrawler crawler, Action<SourceOptions>? configure = null)
    {
        var registry = new SourceRegistry(
            [crawler],
            [new FakeNormalizer("fake")],
            [new FakeSchema()]);

        var options = new SourceOptions();
        configure?.Invoke(options);

        return new CrawlJob(
            registry,
            _documents,
            _runs,
            new StaticOptionsMonitor<SourceOptions>(options),
            NullLogger<CrawlJob>.Instance);
    }
}
