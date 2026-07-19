using FluentAssertions;
using Mealplan.Scraper;

namespace Mealplan.Tests;

public class OneShotCommandTests
{
    [Fact]
    public void A_crawl_with_a_cap_parses()
    {
        var command = OneShotCommand.Parse(["crawl", "gousto", "--max", "5"]);

        command.Should().Be(new OneShotCommand("crawl", "gousto", 5));
    }

    [Fact]
    public void A_crawl_without_a_cap_has_no_limit()
    {
        OneShotCommand.Parse(["crawl", "gousto"])!.MaxDocuments.Should().BeNull();
    }

    [Fact]
    public void Normalize_parses()
    {
        OneShotCommand.Parse(["normalize", "hellofresh"])
            .Should().Be(new OneShotCommand("normalize", "hellofresh", null));
    }

    [Theory]
    [InlineData()]
    [InlineData("crawl")]
    [InlineData("serve", "gousto")]
    [InlineData("--urls", "http://0.0.0.0:5206")]
    public void Anything_else_falls_through_to_the_server(params string[] args)
    {
        // The host is started with arguments of its own, so an unrecognised
        // shape must mean "run normally" rather than fail.
        OneShotCommand.Parse(args).Should().BeNull();
    }

    [Fact]
    public void A_non_numeric_cap_is_ignored_rather_than_crashing()
    {
        OneShotCommand.Parse(["crawl", "gousto", "--max", "lots"])!
            .MaxDocuments.Should().BeNull();
    }
}
