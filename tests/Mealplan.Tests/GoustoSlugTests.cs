using System.Text.Json;
using FluentAssertions;
using Mealplan.Infrastructure.Gousto;
using Mealplan.Infrastructure.Gousto.Api;

namespace Mealplan.Tests;

/// <summary>
/// Driven by a captured list page rather than hand-picked strings. The original
/// version of these tests used urls copied from the *detail* payload, which are
/// single-segment, so a plain trim looked correct. List entries are
/// category-prefixed, and every detail fetch 404'd on the first live crawl.
/// </summary>
public class GoustoListSlugTests
{
    [Theory]
    [InlineData("/chicken-recipes/chicken-date-tamarind-curry", "chicken-date-tamarind-curry")]
    [InlineData("/beef-recipes/simply-perfect-beef-spag-bol", "simply-perfect-beef-spag-bol")]
    [InlineData("/recipes/roast-duck-leg", "roast-duck-leg")]
    [InlineData("/chicken-date-tamarind-curry", "chicken-date-tamarind-curry")]
    [InlineData("chicken-date-tamarind-curry", "chicken-date-tamarind-curry")]
    [InlineData("/deeply/nested/path/final-slug/", "final-slug")]
    public void The_detail_key_is_the_last_path_segment(string url, string expected)
    {
        GoustoCrawler.Slug(new GoustoListEntry("uid", "Title", url)).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("///")]
    public void An_entry_with_no_usable_url_has_no_slug(string? url)
    {
        GoustoCrawler.Slug(new GoustoListEntry("uid", "Title", url)).Should().BeNull();
    }

    [Fact]
    public void Every_entry_on_a_captured_list_page_yields_a_bare_slug()
    {
        var page = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "gousto", "recipes-page.json"));

        using var document = JsonDocument.Parse(page);

        var urls = document.RootElement
            .GetProperty("data").GetProperty("entries")
            .EnumerateArray()
            .Select(e => e.GetProperty("url").GetString())
            .ToList();

        urls.Should().NotBeEmpty();
        urls.Should().Contain(u => u!.Trim('/').Contains('/'),
            "the fixture must include the category-prefixed shape this guards against");

        foreach (var url in urls)
        {
            var slug = GoustoCrawler.Slug(new GoustoListEntry("uid", "Title", url));

            slug.Should().NotBeNull();
            slug.Should().NotContain("/", "the detail endpoint takes a bare slug");
        }
    }
}
