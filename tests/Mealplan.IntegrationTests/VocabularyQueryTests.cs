using FluentAssertions;
using Mealplan.Infrastructure.Reading;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Pins the vocabulary views and their paged queries against the captured
/// payloads. The riskiest line is the HelloFresh traces-of folding: a slug
/// listed here must match v_recipe's arrays exactly, or an exclusion filter
/// built from this list silently misses.
/// </summary>
[Collection(CrossSourceCollection.Name)]
public class VocabularyQueryTests(CrossSourceViewFixture fixture)
{
    [Fact]
    public async Task HelloFresh_traces_entities_fold_into_their_canonical_allergen()
    {
        var allergens = await Query().ListAllergensAsync(sources: ["hellofresh"]);

        // Published as traces-of-mustard only: the canonical slug survives, the
        // prefixed one does not, and the usage lands in trace_count.
        var mustard = allergens.Items.Single(a => a.Slug == "mustard");
        mustard.Name.Should().Be("Mustard");
        mustard.RecipeCount.Should().Be(0, "no fixture recipe confirms mustard as contains");
        mustard.TraceCount.Should().Be(2, "both fixture recipes may contain traces of it");

        // Published both ways: one row carries both counts.
        var soya = allergens.Items.Single(a => a.Slug == "soya");
        soya.RecipeCount.Should().Be(1);
        soya.TraceCount.Should().Be(2);

        allergens.Items.Should().NotContain(a => a.Slug.StartsWith("traces-of-"),
            "a prefixed slug would never match the recipe rows' arrays");
    }

    [Fact]
    public async Task Gousto_trace_counts_are_zero_meaning_unknown_not_none()
    {
        var allergens = await Query().ListAllergensAsync(sources: ["gousto"]);

        allergens.Items.Should().NotBeEmpty();
        allergens.Items.Should().OnlyContain(a => a.TraceCount == 0,
            "gousto publishes no traces data; the capability flag says why");

        var mustard = allergens.Items.Single(a => a.Slug == "mustard");
        mustard.RecipeCount.Should().Be(1, "only the steak sandwich contains mustard");
    }

    [Fact]
    public async Task Every_allergen_slug_on_a_recipe_row_appears_in_the_vocabulary()
    {
        // The list is the authoritative input for excludeAllergens, so it must
        // cover every slug the recipe rows can carry.
        var listed = (await Query().ListAllergensAsync(take: 100)).Items
            .Select(a => (a.Source, a.Slug))
            .ToHashSet();

        var recipes = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });

        foreach (var recipe in recipes.Items)
        {
            foreach (var slug in recipe.Allergens.Concat(recipe.TraceAllergens))
            {
                listed.Should().Contain((recipe.Source, slug));
            }
        }
    }

    [Fact]
    public async Task Cuisines_carry_display_names_and_recipe_counts_per_source()
    {
        var cuisines = await Query().ListCuisinesAsync();

        var italian = cuisines.Items.Single(c => c.Source == "hellofresh" && c.Slug == "italian");
        italian.Name.Should().Be("Italian");
        italian.RecipeCount.Should().Be(2);

        cuisines.Items.Should().Contain(c =>
            c.Source == "gousto" && c.Slug == "british" && c.Name == "British" && c.RecipeCount == 1);
        cuisines.Items.Should().Contain(c =>
            c.Source == "gousto" && c.Slug == "indian" && c.RecipeCount == 1);
    }

    [Fact]
    public async Task Tag_slugs_are_the_tokens_the_recipe_rows_carry()
    {
        var tags = await Query().ListTagsAsync(take: 100);

        // HelloFresh publishes real slugs apart from display names.
        tags.Items.Should().Contain(t =>
            t.Source == "hellofresh" && t.Slug == "quick" && t.Name == "Quick");

        // Gousto categories have no slug, so the title is token and name both -
        // matching what v_recipe's tags array holds.
        var gousto = tags.Items.Single(t => t.Source == "gousto" && t.Name == "All Gousto Recipes");
        gousto.Slug.Should().Be("All Gousto Recipes");
        gousto.RecipeCount.Should().Be(2);
    }

    [Fact]
    public async Task A_tag_slug_read_from_the_list_filters_the_search()
    {
        var hellofresh = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Tags = ["pasta-noodles"],
        });

        hellofresh.Items.Should().NotBeEmpty();
        hellofresh.Items.Should().OnlyContain(r =>
            r.Source == "hellofresh" && r.Tags.Contains("pasta-noodles"));

        var gousto = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Tags = ["Beef Recipes"],
        });

        gousto.Items.Should().ContainSingle().Which.Name.Should().Contain("Steak");
    }

    [Fact]
    public async Task Ingredient_search_resolves_a_household_name_per_source()
    {
        var garlic = await Query().SearchIngredientsAsync("garlic");

        var clove = garlic.Items.Single(i => i.Source == "hellofresh" && i.Name == "Garlic Clove");
        clove.Family.Should().Be("garlic");
        clove.RecipeCount.Should().Be(2);
    }

    [Fact]
    public async Task Gousto_ingredients_carry_no_family_because_none_is_published()
    {
        var ingredients = await Query().SearchIngredientsAsync(sources: ["gousto"], take: 100);

        ingredients.Items.Should().NotBeEmpty();
        ingredients.Items.Should().OnlyContain(i => i.Family == null);
    }

    [Fact]
    public async Task Vocabulary_pages_order_by_usage_and_report_the_full_total()
    {
        var whole = await Query().ListTagsAsync(take: 100);

        whole.Items.Select(t => t.RecipeCount).Should().BeInDescendingOrder();

        var page = await Query().ListTagsAsync(take: 1);

        page.Items.Should().HaveCount(1);
        page.Total.Should().Be(whole.Total);
        page.Items[0].Should().Be(whole.Items[0]);

        var second = await Query().ListTagsAsync(skip: 1, take: 1);
        second.Items[0].Should().Be(whole.Items[1]);
    }

    [Fact]
    public async Task Vocabulary_take_is_clamped_to_the_page_cap()
    {
        var result = await Query().ListAllergensAsync(take: 5000);

        result.Take.Should().Be(100);
    }

    [Fact]
    public async Task Filtering_a_vocabulary_by_source_narrows_it()
    {
        var result = await Query().ListCuisinesAsync(sources: ["hellofresh"]);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(c => c.Source == "hellofresh");
    }

    private RecipeQueryService Query() => fixture.CreateQueryService();
}
