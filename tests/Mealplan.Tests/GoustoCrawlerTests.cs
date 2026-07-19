using FluentAssertions;
using Mealplan.Infrastructure.Gousto;
using Mealplan.Infrastructure.Gousto.Api;

namespace Mealplan.Tests;

public class GoustoCursorTests
{
    [Fact]
    public void A_cursor_round_trips()
    {
        var cursor = new GoustoCursor(48, ["one", "two"]);

        var parsed = GoustoCursor.Parse(cursor.ToJson());

        parsed.Offset.Should().Be(48);
        parsed.PendingSlugs.Should().Equal("one", "two");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"unexpected\":true, \"offset\":")]
    public void An_absent_or_unreadable_cursor_starts_from_the_beginning(string? json)
    {
        // Restarting is safe: unchanged recipes cost one hash comparison each.
        var parsed = GoustoCursor.Parse(json);

        parsed.Offset.Should().Be(0);
        parsed.PendingSlugs.Should().BeEmpty();
    }
}

public class GoustoSlugTests
{
    [Theory]
    [InlineData("/chicken-date-tamarind-curry", "chicken-date-tamarind-curry")]
    [InlineData("chicken-date-tamarind-curry", "chicken-date-tamarind-curry")]
    [InlineData("/simply-perfect-beef-spag-bol/", "simply-perfect-beef-spag-bol")]
    public void The_detail_key_is_the_slug_from_the_url(string url, string expected)
    {
        GoustoCrawler.Slug(new GoustoListEntry("uid", "Title", url)).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_with_no_url_has_no_slug(string? url)
    {
        GoustoCrawler.Slug(new GoustoListEntry("uid", "Title", url)).Should().BeNull();
    }
}

public class GoustoTextTests
{
    [Theory]
    [InlineData("<p>Boil a kettle</p>", "Boil a kettle")]
    [InlineData("<p>One</p><p>Two</p>", "One Two")]
    [InlineData("<p>Peel your <strong>onion[s]</strong> finely</p>", "Peel your onion[s] finely")]
    [InlineData("<p>Line one<br>Line two</p>", "Line one Line two")]
    public void Html_instructions_become_readable_text(string html, string expected)
    {
        GoustoNormalizer.StripHtml(html).Should().Be(expected);
    }

    [Fact]
    public void Adjacent_blocks_do_not_run_their_words_together()
    {
        // Gousto emits blocks with no whitespace between them, so a naive tag
        // strip yields "kettlePeel".
        GoustoNormalizer.StripHtml("<p>Boil a kettle</p><p>Peel your onion</p>")
            .Should().Be("Boil a kettle Peel your onion");
    }

    [Fact]
    public void Html_entities_are_decoded()
    {
        GoustoNormalizer.StripHtml("<p>Salt &amp; pepper</p>").Should().Be("Salt & pepper");
    }
}
