using System.Text.Json;
using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.HelloFresh;
using Mealplan.Infrastructure.HelloFresh.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Runs the normaliser over a payload captured from the live HelloFresh search
/// endpoint. The crawler stores one recipe per document, so the fixture page is
/// split the same way here.
/// </summary>
public class HelloFreshNormalizerTests(HelloFreshFixture postgres)
    : IClassFixture<HelloFreshFixture>, IAsyncLifetime
{
    private const string FirstRecipeId = "6a05bf18204de353958f582d";

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_real_recipe_maps_its_headline_fields()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var recipe = await db.Recipes
            .Include(r => r.Category)
            .SingleAsync(r => r.ExternalId == FirstRecipeId);

        recipe.Name.Should().Be("Miso Prawn, Green Chilli and Yellow Courgette Rigatoni");
        recipe.Slug.Should().Be("miso-prawn-green-chilli-and-yellow-courgette-rigatoni");
        recipe.Headline.Should().Be("with Creamy Kale Sauce and Italian Style Cheese");
        recipe.Category!.Name.Should().Be("Seafood");
        recipe.Difficulty.Should().Be(1);
        recipe.AverageRating.Should().BeApproximately(3.4, 0.001);
    }

    [Fact]
    public async Task Iso_durations_are_stored_as_minutes()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var recipe = await db.Recipes.SingleAsync(r => r.ExternalId == FirstRecipeId);

        recipe.PrepMinutes.Should().Be(20, "the payload says PT20M");
        recipe.TotalMinutes.Should().Be(25, "the payload says PT25M");
    }

    [Fact]
    public async Task Ingredients_carry_real_amounts_and_units()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var lines = await db.Set<HelloFreshYieldIngredientEntity>()
            .Include(x => x.Ingredient)
            .Include(x => x.Yield)
            .Where(x => x.Yield!.Recipe!.ExternalId == FirstRecipeId)
            .ToListAsync();

        lines.Should().NotBeEmpty();
        lines.Should().Contain(x => x.Amount != null, "HelloFresh publishes measured quantities");
        lines.Should().Contain(x => x.Unit != null);
        lines.Should().OnlyContain(x => x.Ingredient != null);
    }

    [Fact]
    public async Task Every_published_portion_count_becomes_a_yield()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var portions = await db.Yields
            .Where(y => y.Recipe!.ExternalId == FirstRecipeId)
            .Select(y => y.Portions)
            .OrderBy(p => p)
            .ToListAsync();

        portions.Should().Equal(2, 3, 4);
    }

    [Fact]
    public async Task Traces_of_allergens_stay_distinct_from_contains()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var links = await db.Set<HelloFreshRecipeAllergenEntity>()
            .Include(x => x.Allergen)
            .Where(x => x.Recipe!.ExternalId == FirstRecipeId)
            .ToListAsync();

        links.Should().NotBeEmpty();
        links.Select(x => x.AllergenId).Should().OnlyHaveUniqueItems(
            "HelloFresh repeats allergens across ingredients");

        // The payload lists both a contains and a traces-of entry; collapsing
        // them would understate what the recipe holds.
        links.Should().Contain(x => !x.TracesOf, "a contains entry must survive deduplication");
    }

    [Fact]
    public async Task Nutrition_is_stored_as_published_names_and_units()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var nutrition = await db.Nutrition
            .Where(n => n.Recipe!.ExternalId == FirstRecipeId)
            .ToListAsync();

        nutrition.Should().Contain(n => n.Name == "Energy (kcal)" && n.Unit == "kcal");
        nutrition.Should().Contain(n => n.Name == "Protein" && n.Unit == "g");
        nutrition.Select(n => n.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Steps_utensils_cuisines_and_tags_are_all_written()
    {
        await using var db = postgres.CreateContext();
        await NormalizeFirst(db);

        var id = await db.Recipes.Where(r => r.ExternalId == FirstRecipeId)
            .Select(r => r.Id).SingleAsync();

        (await db.Steps.CountAsync(s => s.RecipeId == id)).Should().BePositive();
        (await db.Set<HelloFreshRecipeUtensilEntity>().CountAsync(x => x.RecipeId == id))
            .Should().BePositive();
        (await db.Set<HelloFreshRecipeCuisineEntity>().CountAsync(x => x.RecipeId == id))
            .Should().BePositive();
        (await db.Set<HelloFreshRecipeTagEntity>().CountAsync(x => x.RecipeId == id))
            .Should().BePositive();
    }

    [Fact]
    public async Task Renormalising_replaces_children_rather_than_duplicating_them()
    {
        await using var first = postgres.CreateContext();
        await NormalizeFirst(first);

        var id = await first.Recipes.Where(r => r.ExternalId == FirstRecipeId)
            .Select(r => r.Id).SingleAsync();
        var steps = await first.Steps.CountAsync(s => s.RecipeId == id);
        var yields = await first.Yields.CountAsync(y => y.RecipeId == id);

        await using var second = postgres.CreateContext();
        await NormalizeFirst(second);

        (await second.Steps.CountAsync(s => s.RecipeId == id)).Should().Be(steps);
        (await second.Yields.CountAsync(y => y.RecipeId == id)).Should().Be(yields);
        (await second.Recipes.CountAsync(r => r.ExternalId == FirstRecipeId)).Should().Be(1);
    }

    [Fact]
    public async Task Both_recipes_on_the_page_normalise()
    {
        await using var db = postgres.CreateContext();

        foreach (var payload in await RecipePayloads())
        {
            await Normalize(db, payload);
        }

        (await db.Recipes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_payload_with_no_id_fails_loudly()
    {
        await using var db = postgres.CreateContext();
        var normalizer = new HelloFreshNormalizer(db, TimeProvider.System);

        var act = async () => await normalizer.NormalizeAsync(Document("x", """{"name":"No id"}"""));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no id*");
    }

    private static async Task<List<string>> RecipePayloads()
    {
        var page = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "hellofresh", "search-page.json"));

        using var document = JsonDocument.Parse(page);

        return document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetRawText())
            .ToList();
    }

    private static async Task NormalizeFirst(HelloFreshDbContext db) =>
        await Normalize(db, (await RecipePayloads())[0]);

    private static async Task Normalize(HelloFreshDbContext db, string payload)
    {
        using var parsed = JsonDocument.Parse(payload);
        var key = parsed.RootElement.GetProperty("id").GetString()!;

        await new HelloFreshNormalizer(db, TimeProvider.System)
            .NormalizeAsync(Document(key, payload));
    }

    private static ScrapeDocument Document(string key, string payload) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "hellofresh",
        DocumentType = DocumentType.Recipe,
        SourceKey = key,
        Version = 1,
        Payload = payload,
        ContentHash = [],
    };
}
