using FluentAssertions;
using Mealplan.Infrastructure.Sources;
using Mealplan.Tests.Fakes;

namespace Mealplan.Tests;

public class SourceRegistryTests
{
    [Fact]
    public void A_fully_registered_source_resolves()
    {
        var registry = new SourceRegistry(
            [new FakeCrawler("gousto", [])],
            [new FakeNormalizer("gousto")],
            [new FakeSchema("gousto")]);

        registry.Sources.Should().BeEquivalentTo("gousto");
        registry.Crawler("gousto").Source.Should().Be("gousto");
        registry.Normalizer("gousto").Source.Should().Be("gousto");
        registry.Schema("gousto").SchemaName.Should().Be("gousto");
    }

    [Fact]
    public void Slug_matching_ignores_case()
    {
        var registry = new SourceRegistry(
            [new FakeCrawler("gousto", [])],
            [new FakeNormalizer("gousto")],
            [new FakeSchema("gousto")]);

        registry.Crawler("Gousto").Should().NotBeNull();
    }

    [Fact]
    public void A_source_missing_its_normaliser_fails_at_construction()
    {
        var act = () => new SourceRegistry(
            [new FakeCrawler("gousto", [])],
            [],
            [new FakeSchema("gousto")]);

        // Better to refuse to start than to crawl happily and normalise nothing.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*gousto*");
    }

    [Fact]
    public void A_source_missing_its_schema_fails_at_construction()
    {
        var act = () => new SourceRegistry(
            [new FakeCrawler("gousto", [])],
            [new FakeNormalizer("gousto")],
            []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*gousto*");
    }

    [Fact]
    public void An_unknown_source_is_a_clear_error()
    {
        var registry = new SourceRegistry([], [], []);

        var act = () => registry.Crawler("nope");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*nope*");
    }
}
