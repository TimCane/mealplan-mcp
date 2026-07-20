using FluentAssertions;
using Mealplan.Infrastructure.Reading;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Pins each source's projection into v_recipe against the captured payloads.
/// The HelloFresh pivot names and the Gousto basis selection are the riskiest
/// lines in the view SQL, so their numbers are asserted verbatim.
/// </summary>
[Collection(CrossSourceCollection.Name)]
public class ViewProjectionTests(CrossSourceViewFixture fixture)
{
    [Fact]
    public async Task HelloFresh_nutrition_rows_pivot_into_the_panel_by_published_name()
    {
        var recipe = await Find("hellofresh", "rigatoni");

        // The nine row names are pinned strings; a rename upstream must land
        // here as nulls, not as silently shifted numbers.
        recipe.Nutrition.Should().Be(new NutritionPanel(
            Kcal: 851,
            Kj: 3559,
            FatGrams: 40.2,
            SaturatesGrams: 22.4,
            CarbsGrams: 83.5,
            SugarsGrams: 18.6,
            FibreGrams: 9.7,
            ProteinGrams: 38.6,
            SaltGrams: 3.28));
    }

    [Fact]
    public async Task Gousto_contributes_its_per_portion_basis_never_per_hundred_grams()
    {
        var recipe = await Find("gousto", "steak");

        // The fixture's per-100g row says 118 kcal; seeing it here would mean
        // the basis filter broke and the panel mixed bases across sources.
        recipe.Nutrition.Should().Be(new NutritionPanel(
            Kcal: 688,
            Kj: 2888,
            FatGrams: 27.834,
            SaturatesGrams: 7.569,
            CarbsGrams: 70.399,
            SugarsGrams: 11.575,
            FibreGrams: 8.68,
            ProteinGrams: 40.929,
            SaltGrams: 0.9));
    }

    [Fact]
    public async Task Ratings_surface_from_both_sources()
    {
        var gousto = await Find("gousto", "steak");
        var hellofresh = await Find("hellofresh", "rigatoni");

        gousto.RatingAverage.Should().Be(5);
        gousto.RatingCount.Should().Be(14489);
        hellofresh.RatingAverage.Should().Be(3.4);
        hellofresh.RatingCount.Should().Be(4);
    }

    [Fact]
    public async Task Serving_size_is_grams_per_portion_from_each_sources_own_field()
    {
        // Gousto publishes net weight in mg per portion; HelloFresh a grams
        // serving size on the recipe.
        (await Detail("gousto", "steak")).ServingSizeGrams.Should().Be(583);
        (await Detail("hellofresh", "rigatoni")).ServingSizeGrams.Should().Be(791);
    }

    [Fact]
    public async Task Website_urls_come_from_stored_payload_fields_not_slug_guesses()
    {
        (await Detail("gousto", "steak")).WebsiteUrl.Should().Be(
            "https://www.gousto.co.uk/cookbook/beef-recipes/open-steak-sandwich-balsamic-onions-chips");

        (await Detail("hellofresh", "rigatoni")).WebsiteUrl.Should().StartWith(
            "https://www.hellofresh.co.uk/recipes/miso-prawn");
    }

    [Fact]
    public async Task HelloFresh_traces_split_out_of_contains_under_canonical_slugs()
    {
        var recipe = await Find("hellofresh", "rigatoni");

        recipe.Allergens.Should().BeEquivalentTo(
            ["cereals-containing-gluten", "crustaceans", "egg", "milk", "soya", "wheat"],
            "contains must no longer carry the traces entries");

        // Published as traces-of-soya etc; the view strips the prefix so an
        // exclusion on "soya" matches contains and traces alike.
        recipe.TraceAllergens.Should().BeEquivalentTo(
            ["may-contain-traces-of-allergens", "mustard", "soya"]);
    }

    [Fact]
    public async Task Gousto_trace_allergens_are_empty_meaning_unknown_not_none()
    {
        var recipe = await Find("gousto", "steak");

        recipe.Allergens.Should().NotBeEmpty();
        recipe.TraceAllergens.Should().BeEmpty(
            "gousto publishes no traces data and must not pretend otherwise");
    }

    [Fact]
    public async Task Free_text_finds_recipes_by_ingredient_words()
    {
        // Neither word appears in any name, headline or description - only in
        // the ingredient list, so only search_text aggregation can match it.
        (await Find("gousto", "mayonnaise")).Name.Should().Contain("Steak");
        (await Find("hellofresh", "kale")).Name.Should().Contain("Rigatoni");
    }

    [Fact]
    public async Task Detail_reports_every_offered_portion_count_and_upstream_change_time()
    {
        var detail = await Detail("gousto", "steak");

        detail.OfferedPortions.Should().NotBeEmpty()
            .And.BeInAscendingOrder()
            .And.Contain(detail.Portions);
        detail.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Summary_and_detail_agree_on_the_panel()
    {
        var summary = await Find("hellofresh", "rigatoni");
        var detail = await Detail("hellofresh", "rigatoni");

        detail.Nutrition.Should().Be(summary.Nutrition);
        detail.RatingAverage.Should().Be(summary.RatingAverage);
        detail.RatingCount.Should().Be(summary.RatingCount);
        detail.TraceAllergens.Should().Equal(summary.TraceAllergens);
    }

    private async Task<RecipeSummary> Find(string source, string query)
    {
        var page = await fixture.CreateQueryService().SearchAsync(new RecipeSearchQuery
        {
            Query = query,
            Sources = [source],
            Portions = 2,
        });

        page.Items.Should().NotBeEmpty($"'{query}' must match a {source} fixture recipe");
        return page.Items[0];
    }

    private async Task<RecipeDetail> Detail(string source, string query)
    {
        var summary = await Find(source, query);
        return (await fixture.CreateQueryService().GetAsync(source, summary.RecipeId, 2))!;
    }
}
