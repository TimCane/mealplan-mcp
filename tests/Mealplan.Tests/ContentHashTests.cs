using FluentAssertions;
using Mealplan.Infrastructure.Persistence;

namespace Mealplan.Tests;

public class ContentHashTests
{
    [Fact]
    public void Identical_payloads_hash_alike()
    {
        ContentHash.Compute("""{"a":1}""")
            .Should().Equal(ContentHash.Compute("""{"a":1}"""));
    }

    [Theory]
    [InlineData("""{"a":1}""", """{ "a" : 1 }""")]
    [InlineData("""{"a":[1,2]}""", "{\n  \"a\": [\n    1,\n    2\n  ]\n}")]
    public void Whitespace_does_not_change_the_hash(string left, string right)
    {
        ContentHash.Compute(left).Should().Equal(ContentHash.Compute(right));
    }

    [Theory]
    [InlineData("""{"a":1}""", """{"a":2}""")]
    [InlineData("""{"a":1}""", """{"b":1}""")]
    [InlineData("""{"a":"1"}""", """{"a":1}""")]
    public void Differing_content_changes_the_hash(string left, string right)
    {
        ContentHash.Compute(left).Should().NotEqual(ContentHash.Compute(right));
    }

    [Fact]
    public void Key_order_is_treated_as_a_change()
    {
        // Deliberate: re-normalising a reordered payload is cheap, and assuming
        // order is insignificant is not safe for every source.
        ContentHash.Compute("""{"a":1,"b":2}""")
            .Should().NotEqual(ContentHash.Compute("""{"b":2,"a":1}"""));
    }

    [Fact]
    public void Malformed_json_is_hashed_verbatim_rather_than_throwing()
    {
        var act = () => ContentHash.Compute("not json at all");

        act.Should().NotThrow();
        ContentHash.Compute("not json at all")
            .Should().NotEqual(ContentHash.Compute("also not json"));
    }
}
