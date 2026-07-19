using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Jobs;

namespace Mealplan.Tests;

public class StartupCrawlTests
{
    [Fact]
    public void A_source_that_has_never_run_is_seeded()
    {
        // The case this exists for: a fresh deployment would otherwise serve no
        // recipes until its scheduled night, up to a week away.
        StartupCrawl.ShouldSeed(null).Should().BeTrue();
    }

    [Fact]
    public void A_source_that_has_succeeded_is_left_to_the_schedule()
    {
        StartupCrawl.ShouldSeed(Run(ScrapeRunStatus.Succeeded)).Should().BeFalse(
            "a redeploy against an existing database must not start a crawl");
    }

    [Theory]
    [InlineData(ScrapeRunStatus.Failed)]
    [InlineData(ScrapeRunStatus.Cancelled)]
    public void An_interrupted_first_attempt_is_retried(ScrapeRunStatus status)
    {
        // The crawl resumes from its saved cursor, so this costs little.
        StartupCrawl.ShouldSeed(Run(status)).Should().BeTrue();
    }

    [Fact]
    public void A_run_still_marked_running_is_not_duplicated()
    {
        StartupCrawl.ShouldSeed(Run(ScrapeRunStatus.Running)).Should().BeFalse(
            "a second crawl of the same source would duplicate the work");
    }

    private static ScrapeRun Run(ScrapeRunStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "gousto",
        Status = status,
        StartedAt = DateTimeOffset.UnixEpoch,
    };
}
