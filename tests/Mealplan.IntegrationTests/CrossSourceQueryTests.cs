using FluentAssertions;
using Mealplan.Infrastructure.Reading;

namespace Mealplan.IntegrationTests;

public class CrossSourceQueryTests(CrossSourceViewFixture fixture)
    : IClassFixture<CrossSourceViewFixture>
{
    [Fact]
    public async Task Search_returns_recipes_from_both_sources()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });

        result.Recipes.Select(r => r.Source).Distinct()
            .Should().BeEquivalentTo(["gousto", "hellofresh"],
                "the union view is the only place the two sources meet");
    }

    [Fact]
    public async Task Every_row_carries_the_same_shape_whatever_the_source()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });

        result.Recipes.Should().OnlyContain(r => r.Name != string.Empty);
        result.Recipes.Should().OnlyContain(r => r.Portions == 2);
        result.Recipes.Should().OnlyContain(r => r.Cuisines != null && r.Allergens != null);
    }

    [Fact]
    public async Task Full_text_search_matches_on_name()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Query = "steak",
            Portions = 2,
        });

        result.Recipes.Should().NotBeEmpty();
        result.Recipes.Should().Contain(r => r.Name.Contains("Steak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_tolerates_a_typo()
    {
        // Trigram fallback: full text alone would return nothing here.
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Query = "rigatonni",
            Portions = 2,
        });

        result.Recipes.Should().NotBeEmpty("a near miss should still find the recipe");
    }

    [Fact]
    public async Task Excluding_an_allergen_removes_the_recipes_carrying_it()
    {
        var all = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });
        var withGluten = all.Recipes.Where(r => r.Allergens.Contains("gluten")).ToList();

        withGluten.Should().NotBeEmpty("the fixtures must exercise this filter");

        var filtered = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            ExcludeAllergens = ["gluten"],
        });

        filtered.Recipes.Should().NotContain(r => r.Allergens.Contains("gluten"));
        filtered.Recipes.Should().HaveCountLessThan(all.Recipes.Count);
    }

    [Fact]
    public async Task Filtering_by_source_narrows_to_that_source()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Sources = ["gousto"],
        });

        result.Recipes.Should().NotBeEmpty();
        result.Recipes.Should().OnlyContain(r => r.Source == "gousto");
    }

    [Fact]
    public async Task A_recipe_with_no_published_prep_time_is_excluded_rather_than_assumed_quick()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 3,
            MaxPrepMinutes = 120,
        });

        // Gousto publishes prep times only for 2 and 4 portions, so its 3-portion
        // rows have none. Treating null as fast would put them on a weeknight plan.
        result.Recipes.Should().OnlyContain(r => r.PrepMinutes != null);
    }

    [Fact]
    public async Task Including_an_ingredient_requires_it_to_be_present()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            IncludeIngredients = ["garlic"],
        });

        result.Recipes.Should().NotBeEmpty();

        foreach (var recipe in result.Recipes)
        {
            var detail = await Query().GetAsync(recipe.Source, recipe.RecipeId, 2);

            detail!.Ingredients.Should().Contain(
                i => i.Name.Contains("garlic", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Paging_is_stable_and_reports_the_total()
    {
        var first = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2, Take = 1 });
        var second = await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Skip = 1, Take = 1 });

        first.Recipes.Should().HaveCount(1);
        first.Total.Should().BeGreaterThan(1);
        second.Recipes.Should().HaveCount(1);
        second.Recipes[0].RecipeId.Should().NotBe(first.Recipes[0].RecipeId);
    }

    [Fact]
    public async Task HelloFresh_detail_carries_amounts_and_gousto_does_not()
    {
        var hellofresh = await FirstDetail("hellofresh");
        var gousto = await FirstDetail("gousto");

        hellofresh.Ingredients.Should().Contain(i => i.Amount != null && i.Unit != null);
        hellofresh.Notes.HasIngredientQuantities.Should().BeTrue();
        hellofresh.Notes.Caveat.Should().BeNull();

        gousto.Ingredients.Should().NotBeEmpty();
        gousto.Ingredients.Should().OnlyContain(i => i.Amount == null && i.Unit == null);
        gousto.Notes.HasIngredientQuantities.Should().BeFalse();
        gousto.Notes.Caveat.Should().Contain("does not publish ingredient quantities",
            "a null amount must not read as none needed");
    }

    [Fact]
    public async Task Gousto_reports_pantry_items_and_hellofresh_reports_none()
    {
        (await FirstDetail("gousto")).PantryItems.Should().NotBeEmpty();
        (await FirstDetail("hellofresh")).PantryItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Detail_includes_ordered_steps()
    {
        (await FirstDetail("gousto")).Steps.Should().NotBeEmpty();
        (await FirstDetail("hellofresh")).Steps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_unknown_recipe_is_null_rather_than_an_error()
    {
        var detail = await Query().GetAsync("gousto", Guid.CreateVersion7(), 2);

        detail.Should().BeNull();
    }

    [Fact]
    public async Task Asking_for_a_portion_count_a_recipe_lacks_returns_null()
    {
        var recipe = (await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Sources = ["hellofresh"] })).Recipes[0];

        // HelloFresh publishes 2, 3 and 4 portions, never 9.
        (await Query().GetAsync(recipe.Source, recipe.RecipeId, 9)).Should().BeNull();
    }

    [Fact]
    public async Task List_sources_reports_counts_and_capabilities()
    {
        var sources = await Query().ListSourcesAsync();

        sources.Select(s => s.Source).Should().Equal("gousto", "hellofresh");
        sources.Should().OnlyContain(s => s.RecipeCount > 0);

        sources.Single(s => s.Source == "gousto").HasIngredientQuantities.Should().BeFalse();
        sources.Single(s => s.Source == "gousto").HasPantryItems.Should().BeTrue();
        sources.Single(s => s.Source == "hellofresh").HasIngredientQuantities.Should().BeTrue();
    }

    [Fact]
    public async Task Scrape_status_reports_a_row_per_source_even_before_any_crawl()
    {
        var statuses = await Query().ScrapeStatusAsync();

        statuses.Select(s => s.Source).Should().Equal("gousto", "hellofresh");
        statuses.Should().OnlyContain(s => s.LastRunStatus == null,
            "no crawl has run in this fixture");
    }

    private async Task<RecipeDetail> FirstDetail(string source)
    {
        var summary = (await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Sources = [source] })).Recipes[0];

        return (await Query().GetAsync(source, summary.RecipeId, 2))!;
    }

    private RecipeQueryService Query() => fixture.CreateQueryService();
}
