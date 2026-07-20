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
            "search_recipes", "get_recipe", "list_sources", "get_scrape_status",
            "list_allergens", "list_cuisines", "list_tags", "search_ingredients",
            "get_shopping_list");

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

    [Fact]
    public async Task Vocabulary_tools_return_paged_slug_to_name_mappings()
    {
        var allergens = AsJson(await _client.CallToolAsync(
            "list_allergens",
            new Dictionary<string, object?> { ["sources"] = new[] { "hellofresh" } }));

        allergens.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        allergens.GetProperty("items").EnumerateArray()
            .Should().Contain(a =>
                a.GetProperty("slug").GetString() == "mustard"
                && a.GetProperty("traceCount").GetInt32() > 0,
                "the traces-only carrier must reach the wire under its canonical slug");

        var cuisines = AsJson(await _client.CallToolAsync("list_cuisines"));
        cuisines.GetProperty("items").EnumerateArray()
            .Should().Contain(c => c.GetProperty("name").GetString() == "Italian");

        var tags = AsJson(await _client.CallToolAsync("list_tags"));
        var slug = tags.GetProperty("items")[0].GetProperty("slug").GetString();

        var filtered = AsJson(await _client.CallToolAsync(
            "search_recipes",
            new Dictionary<string, object?> { ["tags"] = new[] { slug } }));
        filtered.GetProperty("total").GetInt32()
            .Should().BeGreaterThan(0, "the busiest tag's slug must round-trip into the filter");

        var ingredients = AsJson(await _client.CallToolAsync(
            "search_ingredients",
            new Dictionary<string, object?> { ["query"] = "garlic" }));
        ingredients.GetProperty("items").EnumerateArray()
            .Should().Contain(i => i.GetProperty("name").GetString() == "Garlic Clove");
    }

    [Fact]
    public async Task Shopping_list_round_trips_rows_and_a_bad_ref_names_the_problem()
    {
        var search = AsJson(await _client.CallToolAsync(
            "search_recipes",
            new Dictionary<string, object?> { ["sources"] = new[] { "gousto" } }));
        var recipeId = search.GetProperty("items")[0].GetProperty("recipeId").GetString();

        var rows = AsJson(await _client.CallToolAsync(
                "get_shopping_list",
                new Dictionary<string, object?>
                {
                    ["recipes"] = new[] { new { source = "gousto", recipeId, portions = 2 } },
                }))
            .GetProperty("result");

        rows.EnumerateArray().Should().NotBeEmpty()
            .And.Contain(r => r.GetProperty("isPantryItem").GetBoolean(),
                "the staples the box will not contain must reach the wire flagged");
        rows.EnumerateArray().Should().OnlyContain(
            r => r.GetProperty("recipeName").GetString()!.Length > 0);

        var badPortions = await _client.CallToolAsync(
            "get_shopping_list",
            new Dictionary<string, object?>
            {
                ["recipes"] = new[] { new { source = "gousto", recipeId, portions = 99 } },
            });

        badPortions.IsError.Should().BeTrue();
        badPortions.Content.OfType<TextContentBlock>().Single().Text
            .Should().Contain("Offered portion counts");
    }

    [Fact]
    public async Task Prompts_are_listed_and_render_the_safe_flow()
    {
        var prompts = await _client.ListPromptsAsync();

        prompts.Select(p => p.Name).Should().BeEquivalentTo(
            "plan_week", "find_recipe", "whats_available");

        var planWeek = await _client.GetPromptAsync(
            "plan_week",
            new Dictionary<string, object?> { ["allergens"] = "milk" });

        var text = planWeek.Messages.Select(m => m.Content)
            .OfType<TextContentBlock>().Single().Text;

        text.Should().Contain("list_allergens",
            "the rendered flow must resolve slugs before filtering");

        var orientation = await _client.GetPromptAsync("whats_available");
        orientation.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Completions_serve_live_slugs_from_the_vocabulary_views()
    {
        var completions = await _client.CompleteAsync(
            new PromptReference { Name = "plan_week" },
            argumentName: "allergens",
            argumentValue: "mu");

        completions.Completion.Values.Should().Contain("mustard",
            "the values must come from the live vocabulary, not a static list");

        // A list-shaped argument completes its last segment, carrying the
        // settled ones along.
        var continued = await _client.CompleteAsync(
            new PromptReference { Name = "plan_week" },
            argumentName: "allergens",
            argumentValue: "gluten, mu");

        continued.Completion.Values.Should().Contain("gluten, mustard");
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
