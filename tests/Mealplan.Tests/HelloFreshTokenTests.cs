using System.Text;
using FluentAssertions;
using Mealplan.Infrastructure.HelloFresh;
using Mealplan.Infrastructure.HelloFresh.Api;

namespace Mealplan.Tests;

public class HelloFreshTokenExtractionTests
{
    // Shaped like the real page: the token sits in a serverAuth object inside a
    // large blob of embedded JSON.
    private const string Page = """
        <!DOCTYPE html><html><body><script id="__NEXT_DATA__" type="application/json">
        {"props":{"pageProps":{"hasMultipleLocales":false,"serverAuth":
        {"access_token":"eyJhbGciOiJIUzI1NiJ9.eyJleHAiOjE3ODcwODQ0ODZ9.abc-_123",
        "expires_in":2629743,"token_type":"Bearer"}}}}
        </script></body></html>
        """;

    [Fact]
    public void The_token_is_found_in_the_page()
    {
        HelloFreshTokenProvider.ExtractToken(Page)
            .Should().Be("eyJhbGciOiJIUzI1NiJ9.eyJleHAiOjE3ODcwODQ0ODZ9.abc-_123");
    }

    [Fact]
    public void A_page_without_a_token_yields_null_rather_than_a_wrong_match()
    {
        HelloFreshTokenProvider.ExtractToken("<html><body>no auth here</body></html>")
            .Should().BeNull();
    }

    [Fact]
    public void A_non_jwt_access_token_is_not_matched()
    {
        // Guards against grabbing some unrelated "access_token" that is not the
        // three-part bearer the recipe API wants.
        HelloFreshTokenProvider.ExtractToken("""{"access_token":"not-a-jwt"}""")
            .Should().BeNull();
    }

    [Fact]
    public void The_expiry_comes_from_the_token_claims()
    {
        var now = DateTimeOffset.UnixEpoch;
        var jwt = Jwt("""{"exp":1787084486,"iat":1784454743,"iss":"senf"}""");

        HelloFreshTokenProvider.ExpiryOf(jwt, now)
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(1787084486));
    }

    [Theory]
    [InlineData("not.a.jwt")]
    [InlineData("onlyonepart")]
    [InlineData("two.parts")]
    public void An_unreadable_token_falls_back_to_a_short_life(string jwt)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Refreshing more often than necessary is the safe direction.
        HelloFreshTokenProvider.ExpiryOf(jwt, now).Should().Be(now.AddDays(1));
    }

    [Fact]
    public void A_token_with_no_exp_claim_falls_back_to_a_short_life()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        HelloFreshTokenProvider.ExpiryOf(Jwt("""{"iss":"senf"}"""), now)
            .Should().Be(now.AddDays(1));
    }

    private static string Jwt(string claims)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(claims))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"header.{payload}.signature";
    }
}

public class HelloFreshDurationTests
{
    [Theory]
    [InlineData("PT20M", 20)]
    [InlineData("PT25M", 25)]
    [InlineData("PT1H", 60)]
    [InlineData("PT1H15M", 75)]
    [InlineData("PT0M", 0)]
    public void Iso_durations_become_minutes(string duration, int expected)
    {
        HelloFreshNormalizer.Minutes(duration).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20 minutes")]
    public void An_absent_or_unparseable_duration_is_null_not_zero(string? duration)
    {
        // Zero would read as "no time needed", which is a different claim.
        HelloFreshNormalizer.Minutes(duration).Should().BeNull();
    }
}

public class HelloFreshOptionsTests
{
    [Fact]
    public void The_default_products_filter_covers_the_meal_kit_boxes()
    {
        // Without this filter the search endpoint returns add-on products -
        // loose fruit, yoghurt, a baguette - which are not meals. Live check:
        // 35,084 items unfiltered against 24,545 recipes filtered.
        new HelloFreshOptions().Products.Should()
            .BeEquivalentTo(["classic-box", "veggie-box", "meal-plan", "classic-plan"]);
    }

    [Fact]
    public void The_defaults_target_the_uk_catalogue()
    {
        var options = new HelloFreshOptions();

        options.Country.Should().Be("GB");
        options.Locale.Should().Be("en-GB");
    }
}
