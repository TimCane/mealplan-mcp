using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Hosts the real server in-process and drives it with the SDK's client: the
/// serialization contract agents actually consume, tested where nothing else
/// covers it.
/// </summary>
[Collection(CrossSourceCollection.Name)]
public sealed class McpRoundTripTests(CrossSourceViewFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Mealplan", fixture.ConnectionString));

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") },
            _factory.CreateClient(),
            ownsHttpClient: false);

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public void Initialize_delivers_identity_and_instructions()
    {
        _client.ServerInfo.Name.Should().Be("mealplan");

        // The instructions are the one channel guaranteed to reach every agent
        // before its first tool call, so the core contract must be in them.
        _client.ServerInstructions.Should().Contain("null")
            .And.Contain("per portion");
    }

    [Fact]
    public async Task Every_tool_is_listed_read_only_with_an_output_schema()
    {
        var tools = await _client.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            "search_recipes", "get_recipe", "list_sources", "get_scrape_status");

        foreach (var tool in tools)
        {
            var annotations = tool.ProtocolTool.Annotations;

            annotations.Should().NotBeNull($"{tool.Name} must carry hints");
            annotations!.ReadOnlyHint.Should().BeTrue();
            annotations.IdempotentHint.Should().BeTrue();
            annotations.OpenWorldHint.Should().BeFalse();

            tool.ProtocolTool.OutputSchema.Should().NotBeNull(
                $"{tool.Name} must return structured content with a schema");
        }
    }

    [Fact]
    public async Task Search_returns_structured_pages_with_the_nutrition_panel()
    {
        var result = await _client.CallToolAsync(
            "search_recipes",
            new Dictionary<string, object?>
            {
                ["sort"] = "rating",
                ["minRating"] = 1,
                ["nutrientFilters"] = new[] { new { nutrient = "protein", min = 10 } },
            });

        result.IsError.Should().NotBeTrue();

        var page = AsJson(result);
        page.GetProperty("total").GetInt32().Should().BeGreaterThan(0);

        var first = page.GetProperty("items")[0];
        first.GetProperty("nutrition").GetProperty("proteinGrams").GetDouble()
            .Should().BeGreaterThanOrEqualTo(10);
        first.GetProperty("ratingAverage").GetDouble().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Get_recipe_round_trips_and_a_bad_portion_count_names_the_fix()
    {
        var search = AsJson(await _client.CallToolAsync(
            "search_recipes",
            new Dictionary<string, object?> { ["sources"] = new[] { "hellofresh" } }));

        var item = search.GetProperty("items")[0];
        var recipeId = item.GetProperty("recipeId").GetString();

        var detail = AsJson(await _client.CallToolAsync(
            "get_recipe",
            new Dictionary<string, object?>
            {
                ["source"] = "hellofresh",
                ["recipeId"] = recipeId,
                ["portions"] = 2,
            }));

        detail.GetProperty("offeredPortions").EnumerateArray().Should().NotBeEmpty();
        detail.GetProperty("websiteUrl").GetString().Should().StartWith("https://");
        detail.GetProperty("traceAllergens").ValueKind.Should().Be(JsonValueKind.Array,
            "the traces split must reach the wire, not just the read model");
        detail.GetProperty("utensils").EnumerateArray().Should().NotBeEmpty();

        var wrongPortions = await _client.CallToolAsync(
            "get_recipe",
            new Dictionary<string, object?>
            {
                ["source"] = "hellofresh",
                ["recipeId"] = recipeId,
                ["portions"] = 9,
            });

        wrongPortions.IsError.Should().BeTrue();
        wrongPortions.Content.OfType<TextContentBlock>().Single().Text
            .Should().Contain("Offered portion counts");
    }

    [Fact]
    public async Task Source_and_status_tools_answer_one_row_per_source()
    {
        // List-returning tools arrive wrapped: structuredContent must be an
        // object, so the SDK nests bare arrays under "result".
        var sources = AsJson(await _client.CallToolAsync("list_sources"))
            .GetProperty("result");
        var status = AsJson(await _client.CallToolAsync("get_scrape_status"))
            .GetProperty("result");

        sources.EnumerateArray().Select(s => s.GetProperty("source").GetString())
            .Should().Equal("gousto", "hellofresh");
        status.EnumerateArray().Should().HaveCount(2);
    }

    /// <summary>
    /// Reads the structuredContent payload - the shape schema-aware agents
    /// consume, which is exactly what these tests exist to pin.
    /// </summary>
    private static JsonElement AsJson(CallToolResult result)
    {
        result.IsError.Should().NotBeTrue(
            result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        result.StructuredContent.Should().NotBeNull();

        return result.StructuredContent!.Value;
    }
}
