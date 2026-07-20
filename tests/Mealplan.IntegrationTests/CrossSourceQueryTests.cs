using FluentAssertions;
using Mealplan.Infrastructure.Reading;

namespace Mealplan.IntegrationTests;

[Collection(CrossSourceCollection.Name)]
public class CrossSourceQueryTests(CrossSourceViewFixture fixture)
{
    [Fact]
    public async Task Search_returns_recipes_from_both_sources()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });

        result.Items.Select(r => r.Source).Distinct()
            .Should().BeEquivalentTo(["gousto", "hellofresh"],
                "the union view is the only place the two sources meet");
    }

    [Fact]
    public async Task Every_row_carries_the_same_shape_whatever_the_source()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });

        result.Items.Should().OnlyContain(r => r.Name != string.Empty);
        result.Items.Should().OnlyContain(r => r.Portions == 2);
        result.Items.Should().OnlyContain(r =>
            r.Cuisines != null && r.Allergens != null && r.TraceAllergens != null);
    }

    [Fact]
    public async Task Full_text_search_matches_on_name()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Query = "steak",
            Portions = 2,
        });

        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(r => r.Name.Contains("Steak", StringComparison.OrdinalIgnoreCase));
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

        result.Items.Should().NotBeEmpty("a near miss should still find the recipe");
    }

    [Fact]
    public async Task Excluding_an_allergen_removes_the_recipes_carrying_it()
    {
        var all = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });
        var withGluten = all.Items.Where(r => r.Allergens.Contains("gluten")).ToList();

        withGluten.Should().NotBeEmpty("the fixtures must exercise this filter");

        var filtered = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            ExcludeAllergens = ["gluten"],
        });

        filtered.Items.Should().NotContain(r => r.Allergens.Contains("gluten"));
        filtered.Items.Should().HaveCountLessThan(all.Items.Count);
    }

    [Fact]
    public async Task Excluding_an_allergen_matches_traces_by_default()
    {
        // Both HelloFresh fixtures carry mustard only as may-contain-traces;
        // the Gousto steak sandwich contains it outright.
        var strict = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            ExcludeAllergens = ["mustard"],
        });

        strict.Items.Should().NotBeEmpty("the filter must not empty the catalogue");
        strict.Items.Should().NotContain(r => r.Source == "hellofresh",
            "a traces-only carrier is excluded under the safe default");
        strict.Items.Should().NotContain(r =>
            r.Allergens.Contains("mustard") || r.TraceAllergens.Contains("mustard"));
    }

    [Fact]
    public async Task Relaxing_excludeTraces_narrows_the_filter_to_confirmed_contains()
    {
        var relaxed = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            ExcludeAllergens = ["mustard"],
            ExcludeTraces = false,
        });

        relaxed.Items.Should().Contain(r => r.TraceAllergens.Contains("mustard"),
            "traces-only carriers return when the caller relaxes the default");
        relaxed.Items.Should().NotContain(r => r.Allergens.Contains("mustard"),
            "confirmed carriers stay excluded either way");
    }

    [Fact]
    public async Task Filtering_by_source_narrows_to_that_source()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Sources = ["gousto"],
        });

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(r => r.Source == "gousto");
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
        result.Items.Should().OnlyContain(r => r.PrepMinutes != null);
    }

    [Fact]
    public async Task Including_an_ingredient_requires_it_to_be_present()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            IncludeIngredients = ["garlic"],
        });

        result.Items.Should().NotBeEmpty();

        foreach (var recipe in result.Items)
        {
            var detail = await Query().GetAsync(recipe.Source, recipe.RecipeId, 2);

            detail!.Ingredients.Should().Contain(
                i => i.Name.Contains("garlic", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Excluding_an_ingredient_strikes_every_recipe_containing_it()
    {
        var with = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            IncludeIngredients = ["garlic"],
        });

        with.Items.Should().NotBeEmpty("the fixtures must exercise this filter");

        var without = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            ExcludeIngredients = ["garlic"],
        });

        without.Items.Select(r => r.RecipeId)
            .Should().NotIntersectWith(with.Items.Select(r => r.RecipeId));

        // One needle includes on contains and excludes on not-contains, so the
        // two searches must partition the catalogue exactly.
        var all = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2 });
        (with.Total + without.Total).Should().Be(all.Total);
    }

    [Fact]
    public async Task A_recipe_with_no_published_total_time_is_excluded_rather_than_assumed_quick()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 3,
            MaxTotalMinutes = 240,
        });

        // Gousto derives total time from prep time, which it publishes only
        // for 2 and 4 portions - its 3-portion rows must drop out even under
        // a cap this generous.
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(r => r.TotalMinutes != null && r.TotalMinutes <= 240);
    }

    [Fact]
    public async Task Paging_is_stable_and_reports_the_total()
    {
        var first = await Query().SearchAsync(new RecipeSearchQuery { Portions = 2, Take = 1 });
        var second = await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Skip = 1, Take = 1 });

        first.Items.Should().HaveCount(1);
        first.Total.Should().BeGreaterThan(1);
        second.Items.Should().HaveCount(1);
        second.Items[0].RecipeId.Should().NotBe(first.Items[0].RecipeId);
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
    public async Task HelloFresh_lists_utensils_and_gousto_reports_them_unpublished()
    {
        var hellofresh = await FirstDetail("hellofresh");
        var gousto = await FirstDetail("gousto");

        hellofresh.Utensils.Should().NotBeEmpty();
        hellofresh.Notes.HasUtensils.Should().BeTrue();

        gousto.Utensils.Should().BeEmpty();
        gousto.Notes.HasUtensils.Should().BeFalse(
            "an empty list from a source that publishes none must be attributable");
    }

    [Fact]
    public async Task Gousto_notes_state_that_traces_are_unpublished()
    {
        var gousto = await FirstDetail("gousto");

        gousto.Notes.HasTraceAllergens.Should().BeFalse();
        gousto.Notes.Caveat.Should().Contain("may-contain-traces",
            "an empty traces list must read as unknown, not none");

        var hellofresh = await FirstDetail("hellofresh");

        hellofresh.Notes.HasTraceAllergens.Should().BeTrue();
        hellofresh.Notes.Caveat.Should().BeNull();
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
    public async Task Asking_for_a_portion_count_a_recipe_lacks_names_the_counts_that_work()
    {
        var recipe = (await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Sources = ["hellofresh"] })).Items[0];

        // HelloFresh publishes 2, 3 and 4 portions, never 9. Unlike an unknown
        // id, this fails with the offered counts so the caller learns the fix.
        var act = () => Query().GetAsync(recipe.Source, recipe.RecipeId, 9);

        (await act.Should().ThrowAsync<PortionsNotOfferedException>())
            .Which.OfferedPortions.Should().Equal(2, 3, 4);
    }

    [Fact]
    public async Task List_sources_reports_counts_and_capabilities()
    {
        var sources = await Query().ListSourcesAsync();

        sources.Select(s => s.Source).Should().Equal("gousto", "hellofresh");
        sources.Should().OnlyContain(s => s.RecipeCount > 0);

        var gousto = sources.Single(s => s.Source == "gousto");
        var hellofresh = sources.Single(s => s.Source == "hellofresh");

        gousto.HasIngredientQuantities.Should().BeFalse();
        gousto.HasPantryItems.Should().BeTrue();
        gousto.HasTraceAllergens.Should().BeFalse();
        gousto.HasUtensils.Should().BeFalse();

        hellofresh.HasIngredientQuantities.Should().BeTrue();
        hellofresh.HasTraceAllergens.Should().BeTrue();
        hellofresh.HasUtensils.Should().BeTrue();
    }

    [Fact]
    public async Task Scrape_status_reports_a_row_per_source_even_before_any_crawl()
    {
        var statuses = await Query().ScrapeStatusAsync();

        statuses.Select(s => s.Source).Should().Equal("gousto", "hellofresh");
        statuses.Should().OnlyContain(s => s.LastRunStatus == null,
            "no crawl has run in this fixture");
    }

    [Fact]
    public async Task Nutrient_filters_bound_the_range_and_exclude_unpublished_values()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            NutrientFilters = [new NutrientFilter(Nutrient.Protein, Min: 40)],
        });

        // The steak sandwich publishes 40.9g protein; the rigatoni's 38.6g
        // falls under the bound.
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(r =>
            r.Nutrition.ProteinGrams != null && r.Nutrition.ProteinGrams >= 40);

        var band = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            NutrientFilters = [new NutrientFilter(Nutrient.Kcal, Min: 600, Max: 700)],
        });

        band.Items.Should().NotBeEmpty();
        band.Items.Should().OnlyContain(r =>
            r.Nutrition.Kcal != null && r.Nutrition.Kcal >= 600 && r.Nutrition.Kcal <= 700);
    }

    [Fact]
    public async Task Min_rating_excludes_recipes_rated_below_it()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            MinRating = 1,
        });

        // The campfire orzotto fixture carries a 0 rating and must drop out.
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(r => r.RatingAverage != null && r.RatingAverage >= 1);
    }

    [Fact]
    public async Task Rating_sort_puts_the_best_rated_first()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Sort = RecipeSort.Rating,
        });

        result.Items.Select(r => r.RatingAverage ?? -1).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Kcal_sort_puts_the_lightest_first()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Sort = RecipeSort.Kcal,
        });

        result.Items.Select(r => r.Nutrition.Kcal ?? double.MaxValue)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Seeded_random_sort_is_stable_within_a_seed_and_pages_without_tearing()
    {
        RecipeSearchQuery Seeded(int skip, int take) => new()
        {
            Portions = 2,
            Sort = RecipeSort.Random,
            Seed = 7,
            Skip = skip,
            Take = take,
        };

        var whole = (await Query().SearchAsync(Seeded(0, 100))).Items;
        var again = (await Query().SearchAsync(Seeded(0, 100))).Items;

        again.Select(r => r.RecipeId).Should().Equal(whole.Select(r => r.RecipeId),
            "the same seed must draw the same shuffle");

        var pageSize = 2;
        var paged = new List<Guid>();

        for (var skip = 0; skip < whole.Count; skip += pageSize)
        {
            paged.AddRange((await Query().SearchAsync(Seeded(skip, pageSize)))
                .Items.Select(r => r.RecipeId));
        }

        paged.Should().Equal(whole.Select(r => r.RecipeId),
            "pages read one at a time must compose to the whole shuffle");
    }

    [Fact]
    public async Task Take_is_clamped_to_the_page_cap()
    {
        var result = await Query().SearchAsync(new RecipeSearchQuery
        {
            Portions = 2,
            Take = 5000,
        });

        result.Take.Should().Be(100, "no caller may ask for the world in one page");
    }

    private async Task<RecipeDetail> FirstDetail(string source)
    {
        var summary = (await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Sources = [source] })).Items[0];

        return (await Query().GetAsync(source, summary.RecipeId, 2))!;
    }

    private RecipeQueryService Query() => fixture.CreateQueryService();
}
