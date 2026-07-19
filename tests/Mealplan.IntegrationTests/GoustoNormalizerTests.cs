using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Gousto;
using Mealplan.Infrastructure.Gousto.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Runs the normaliser over payloads captured from the live Gousto API, so a
/// change in what Gousto actually returns shows up here rather than in
/// production.
/// </summary>
public class GoustoNormalizerTests(GoustoFixture postgres)
    : IClassFixture<GoustoFixture>, IAsyncLifetime
{
    private const string SteakSlug = "open-steak-sandwich-balsamic-onions-chips";
    private const string CurrySlug = "chicken-date-tamarind-curry";

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_real_recipe_maps_its_headline_fields()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, SteakSlug, "recipe-open-steak-sandwich.json");

        var recipe = await db.Recipes
            .Include(r => r.Cuisine)
            .SingleAsync(r => r.Slug == SteakSlug);

        recipe.Title.Should().Be("Open Steak Sandwich With Balsamic Onions And Chips");
        recipe.Cuisine!.Slug.Should().Be("british");
        recipe.RatingAverage.Should().Be(5);
        recipe.RatingCount.Should().Be(14489);
        recipe.ImageUrl.Should().StartWith("https://");
    }

    [Fact]
    public async Task Portion_sizes_become_yields_and_prep_time_only_exists_for_two_and_four()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, SteakSlug, "recipe-open-steak-sandwich.json");

        var yields = await db.Yields
            .Where(y => y.Recipe!.Slug == SteakSlug)
            .OrderBy(y => y.Portions)
            .ToListAsync();

        yields.Should().NotBeEmpty();
        yields.Select(y => y.Portions).Should().BeInAscendingOrder();

        var withPrep = yields.Where(y => y.PrepMinutes is not null).Select(y => y.Portions);
        withPrep.Should().BeSubsetOf(new[] { 2, 4 },
            "Gousto publishes prep_times only as for_2 and for_4");

        yields.Where(y => y.Portions is not (2 or 4))
            .Should().OnlyContain(y => y.PrepMinutes == null,
                "an unpublished prep time must stay null rather than borrow another portion's");
    }

    [Fact]
    public async Task Ingredients_join_through_portion_skus_and_carry_no_quantities()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, SteakSlug, "recipe-open-steak-sandwich.json");

        var links = await db.Set<GoustoYieldIngredientEntity>()
            .Include(yi => yi.Ingredient)
            .Include(yi => yi.Yield)
            .Where(yi => yi.Yield!.Recipe!.Slug == SteakSlug)
            .ToListAsync();

        links.Should().NotBeEmpty("portion SKUs must resolve to catalogue ingredients");
        links.Should().OnlyContain(yi => yi.Ingredient != null);
        links.Should().Contain(yi => yi.SkuCode != null);
        links.Should().Contain(yi => yi.InBox != null);
    }

    [Fact]
    public async Task The_ingredient_label_is_kept_verbatim_rather_than_parsed()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, CurrySlug, "recipe-chicken-date-tamarind-curry.json");

        var labels = await db.Ingredients
            .Where(i => i.Label != null)
            .Select(i => i.Label!)
            .ToListAsync();

        // Some labels carry a weight in free text, some carry "x0". Neither is a
        // structured quantity, so both are stored as written.
        labels.Should().Contain(l => l.Contains('('), "weights in labels are preserved");
        labels.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Allergens_union_the_recipe_level_and_ingredient_level_lists()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, SteakSlug, "recipe-open-steak-sandwich.json");

        var slugs = await db.Set<GoustoRecipeAllergenEntity>()
            .Include(ra => ra.Allergen)
            .Where(ra => ra.Recipe!.Slug == SteakSlug)
            .Select(ra => ra.Allergen!.Slug)
            .ToListAsync();

        slugs.Should().Contain(["sulphites", "mustard", "gluten", "egg"]);
        slugs.Should().OnlyHaveUniqueItems("a recipe must not list an allergen twice");
    }

    [Fact]
    public async Task Pantry_basics_are_kept_apart_from_shipped_ingredients()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, CurrySlug, "recipe-chicken-date-tamarind-curry.json");

        var pantry = await db.PantryItems
            .Where(p => p.Recipe!.Slug == CurrySlug)
            .Select(p => p.Slug)
            .ToListAsync();

        pantry.Should().Contain("butter");
        pantry.Should().Contain("olive-oil");

        var ingredientNames = await db.Ingredients.Select(i => i.Name).ToListAsync();
        ingredientNames.Should().NotContain("Butter",
            "basics are supplied by the cook, not shipped in the box");
    }

    [Fact]
    public async Task Steps_are_ordered_and_available_as_plain_text()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, CurrySlug, "recipe-chicken-date-tamarind-curry.json");

        var steps = await db.Steps
            .Where(s => s.Recipe!.Slug == CurrySlug)
            .OrderBy(s => s.Order)
            .ToListAsync();

        steps.Should().NotBeEmpty();
        steps.Select(s => s.Order).Should().BeInAscendingOrder();
        steps[0].InstructionHtml.Should().Contain("<p>");
        steps[0].InstructionText.Should().NotContain("<");
        steps[0].InstructionText.Should().Contain("Boil a kettle");
    }

    [Fact]
    public async Task Nutrition_is_converted_from_milligrams_to_grams()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, CurrySlug, "recipe-chicken-date-tamarind-curry.json");

        var perPortion = await db.Nutrition
            .SingleAsync(n => n.Recipe!.Slug == CurrySlug && n.Basis == NutritionBasis.PerPortion);

        perPortion.EnergyKcal.Should().Be(571);
        perPortion.ProteinGrams.Should().BeApproximately(35.754, 0.001);
        perPortion.SaltGrams.Should().BeApproximately(1.802, 0.001);
    }

    [Fact]
    public async Task Renormalising_replaces_children_rather_than_duplicating_them()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, SteakSlug, "recipe-open-steak-sandwich.json");

        var firstYields = await db.Yields.CountAsync(y => y.Recipe!.Slug == SteakSlug);
        var firstSteps = await db.Steps.CountAsync(s => s.Recipe!.Slug == SteakSlug);

        await using var second = postgres.CreateContext();
        await Normalize(second, SteakSlug, "recipe-open-steak-sandwich.json");

        (await second.Yields.CountAsync(y => y.Recipe!.Slug == SteakSlug)).Should().Be(firstYields);
        (await second.Steps.CountAsync(s => s.Recipe!.Slug == SteakSlug)).Should().Be(firstSteps);
        (await second.Recipes.CountAsync(r => r.Slug == SteakSlug)).Should().Be(1);
    }

    [Fact]
    public async Task Every_child_row_is_actually_written()
    {
        await using var db = postgres.CreateContext();
        await Normalize(db, SteakSlug, "recipe-open-steak-sandwich.json");

        // Children carry client-set GUID keys. Attaching one to a tracked
        // parent's navigation makes EF treat it as an existing row and emit an
        // UPDATE that matches nothing, so the row silently never appears. Each
        // child is added to its DbSet explicitly; this asserts the outcome.
        var recipeId = await db.Recipes.Where(r => r.Slug == SteakSlug)
            .Select(r => r.Id).SingleAsync();

        (await db.Yields.CountAsync(y => y.RecipeId == recipeId))
            .Should().BePositive("portion sizes must be written");
        (await db.Steps.CountAsync(s => s.RecipeId == recipeId))
            .Should().BePositive("steps must be written");
        (await db.Nutrition.CountAsync(n => n.RecipeId == recipeId))
            .Should().BePositive("nutrition must be written");
        (await db.Set<GoustoRecipeAllergenEntity>().CountAsync(ra => ra.RecipeId == recipeId))
            .Should().BePositive("allergen links must be written");
        (await db.Set<GoustoRecipeCategoryEntity>().CountAsync(rc => rc.RecipeId == recipeId))
            .Should().BePositive("category links must be written");
    }

    [Fact]
    public async Task A_payload_with_no_entry_fails_loudly()
    {
        await using var db = postgres.CreateContext();
        var normalizer = new GoustoNormalizer(db, TimeProvider.System);

        var act = async () => await normalizer.NormalizeAsync(Document("nope", """{"data":{}}"""));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nope*");
    }

    [Fact]
    public void Only_recipe_documents_are_handled()
    {
        using var db = postgres.CreateContext();
        var normalizer = new GoustoNormalizer(db, TimeProvider.System);

        normalizer.Handles.Should().Equal(DocumentType.Recipe);
        normalizer.Handles.Should().NotContain(DocumentType.RecipeSummary,
            "list pages exist to discover slugs, not to be normalised");
    }

    private static async Task Normalize(GoustoDbContext db, string slug, string fixture)
    {
        var payload = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "gousto", fixture));

        await new GoustoNormalizer(db, TimeProvider.System)
            .NormalizeAsync(Document(slug, payload));
    }

    private static ScrapeDocument Document(string slug, string payload) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "gousto",
        DocumentType = DocumentType.Recipe,
        SourceKey = slug,
        Version = 1,
        Payload = payload,
        ContentHash = [],
    };
}
