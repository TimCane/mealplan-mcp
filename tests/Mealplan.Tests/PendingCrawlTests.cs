using FluentAssertions;
using Hangfire.Common;
using Mealplan.Infrastructure.Jobs;
using Mealplan.Scraper;
using Mealplan.Tests.Fakes;

namespace Mealplan.Tests;

public class PendingCrawlTests
{
    private readonly FakeMonitoringApi monitoring = new();

    [Fact]
    public void Empty_storage_has_no_pending_crawl()
    {
        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeFalse();
    }

    [Fact]
    public void A_queued_crawl_of_the_source_is_pending()
    {
        monitoring.Enqueued["crawl"] = [Crawl("gousto")];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeTrue(
            "the interrupted job resumes by itself; seeding next to it runs the crawl twice");
    }

    [Fact]
    public void A_crawl_of_another_source_is_not_pending()
    {
        monitoring.Enqueued["crawl"] = [Crawl("hellofresh")];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeFalse();
    }

    [Fact]
    public void A_queued_normalise_of_the_source_is_not_a_crawl()
    {
        monitoring.Enqueued["normalize"] = [Normalize("gousto")];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeFalse();
    }

    [Fact]
    public void A_crawl_being_worked_is_pending()
    {
        monitoring.Processing.Add(Crawl("gousto"));

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeTrue();
    }

    [Fact]
    public void A_crawl_a_worker_has_fetched_is_pending()
    {
        monitoring.Fetched["crawl"] = [Crawl("gousto")];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeTrue();
    }

    [Fact]
    public void A_crawl_waiting_on_a_retry_is_pending()
    {
        monitoring.Scheduled.Add(Crawl("gousto"));

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeTrue(
            "a failed crawl scheduled for retry still runs without help");
    }

    [Fact]
    public void Source_names_match_case_insensitively()
    {
        monitoring.Enqueued["crawl"] = [Crawl("Gousto")];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeTrue();
    }

    [Fact]
    public void A_job_that_no_longer_deserialises_is_skipped()
    {
        monitoring.Enqueued["crawl"] = [null];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeFalse();
    }

    [Fact]
    public void The_search_pages_past_the_first_batch()
    {
        monitoring.Enqueued["crawl"] =
            [.. Enumerable.Repeat(Crawl("hellofresh"), 250), Crawl("gousto")];

        PendingCrawl.ExistsFor(monitoring, "gousto").Should().BeTrue();
    }

    private static Job Crawl(string source) =>
        Job.FromExpression<CrawlJob>(job => job.RunAsync(source, null, CancellationToken.None));

    private static Job Normalize(string source) =>
        Job.FromExpression<NormalizeJob>(job => job.RunAsync(source, 200, CancellationToken.None));
}
