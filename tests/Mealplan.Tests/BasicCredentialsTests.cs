using System.Text;
using FluentAssertions;
using Mealplan.Scraper;

namespace Mealplan.Tests;

public class BasicCredentialsTests
{
    [Fact]
    public void The_configured_credentials_are_accepted()
    {
        BasicCredentials.Match(Header("scraper", "hunter2"), Expected).Should().BeTrue();
    }

    [Theory]
    [InlineData("scraper", "wrong")]
    [InlineData("wrong", "hunter2")]
    [InlineData("wrong", "wrong")]
    public void A_wrong_half_is_rejected(string username, string password)
    {
        BasicCredentials.Match(Header(username, password), Expected).Should().BeFalse();
    }

    [Fact]
    public void Credentials_are_compared_case_sensitively()
    {
        BasicCredentials.Match(Header("Scraper", "Hunter2"), Expected).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc123")]
    [InlineData("Basic")]
    [InlineData("Basic not-base64!")]
    public void A_header_that_is_not_basic_auth_is_rejected(string? header)
    {
        BasicCredentials.Match(header, Expected).Should().BeFalse();
    }

    [Fact]
    public void A_payload_with_no_colon_is_rejected()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("scraperhunter2"));

        BasicCredentials.Match($"Basic {encoded}", Expected).Should().BeFalse();
    }

    [Fact]
    public void A_password_containing_a_colon_survives_the_split()
    {
        // Only the first colon separates the two, so a generated password is not
        // silently truncated.
        var expected = new JobsDashboardOptions { Username = "scraper", Password = "a:b:c" };

        BasicCredentials.Match(Header("scraper", "a:b:c"), expected).Should().BeTrue();
    }

    [Fact]
    public void The_scheme_is_matched_case_insensitively()
    {
        // RFC 7235 makes the scheme token case insensitive, and clients vary.
        BasicCredentials.Match(Header("scraper", "hunter2", "basic"), Expected).Should().BeTrue();
    }

    [Fact]
    public void Unconfigured_credentials_match_nothing()
    {
        var empty = new JobsDashboardOptions();

        BasicCredentials.Match(Header(string.Empty, string.Empty), empty).Should().BeFalse(
            "an empty expectation must never be satisfiable");
    }

    private static JobsDashboardOptions Expected =>
        new() { Username = "scraper", Password = "hunter2" };

    private static string Header(string username, string password, string scheme = "Basic")
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        return $"{scheme} {encoded}";
    }
}
