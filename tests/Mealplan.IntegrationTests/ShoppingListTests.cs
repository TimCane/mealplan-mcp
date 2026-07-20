using FluentAssertions;
using Mealplan.Infrastructure.Reading;

namespace Mealplan.IntegrationTests;

[Collection(CrossSourceCollection.Name)]
public class ShoppingListTests(CrossSourceViewFixture fixture)
{
    [Fact]
    public async Task Rows_are_tagged_by_recipe_and_pantry_items_are_flagged()
    {
        var gousto = await FirstSummary("gousto");
        var hellofresh = await FirstSummary("hellofresh");

        var rows = await Query().ShoppingListAsync(
        [
            new ShoppingListRef("gousto", gousto.RecipeId, 2),
            new ShoppingListRef("hellofresh", hellofresh.RecipeId, 2),
        ]);

        rows.Should().OnlyContain(r => r.RecipeName.Length > 0,
            "every row must say which recipe it shops for");
        rows.Select(r => r.Source).Distinct()
            .Should().BeEquivalentTo(["gousto", "hellofresh"]);

        var goustoRows = rows.Where(r => r.Source == "gousto").ToList();
        var helloFreshRows = rows.Where(r => r.Source == "hellofresh").ToList();

        // Gousto ships boxes: shipped rows carry no amounts, and the pantry
        // staples the box will not contain arrive flagged.
        goustoRows.Should().Contain(r => r.IsPantryItem);
        goustoRows.Should().OnlyContain(r => r.Amount == null && r.Unit == null);

        // HelloFresh publishes measured amounts and no pantry list.
        helloFreshRows.Should().Contain(r => r.Amount != null && r.Unit != null);
        helloFreshRows.Should().OnlyContain(r => !r.IsPantryItem);
    }

    [Fact]
    public async Task The_same_recipe_is_never_merged_with_another()
    {
        var gousto = await FirstSummary("gousto");
        var hellofresh = await FirstSummary("hellofresh");

        var alone = await Query().ShoppingListAsync(
            [new ShoppingListRef("gousto", gousto.RecipeId, 2)]);
        var together = await Query().ShoppingListAsync(
        [
            new ShoppingListRef("gousto", gousto.RecipeId, 2),
            new ShoppingListRef("hellofresh", hellofresh.RecipeId, 2),
        ]);

        // Cross-source ingredient identity is deliberately unsolved: adding a
        // second recipe must only append its rows, never collapse shared
        // ingredients into one.
        together.Where(r => r.Source == "gousto").Should().BeEquivalentTo(alone);
    }

    [Fact]
    public async Task An_unknown_recipe_fails_the_whole_call()
    {
        var act = () => Query().ShoppingListAsync(
            [new ShoppingListRef("gousto", Guid.CreateVersion7(), 2)]);

        // A shopping list silently missing a night's ingredients is worse than
        // an error, so a bad ref refuses the lot.
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task A_wrong_portion_count_fails_naming_the_counts_that_work()
    {
        var hellofresh = await FirstSummary("hellofresh");

        var act = () => Query().ShoppingListAsync(
            [new ShoppingListRef("hellofresh", hellofresh.RecipeId, 9)]);

        (await act.Should().ThrowAsync<PortionsNotOfferedException>())
            .Which.OfferedPortions.Should().Equal(2, 3, 4);
    }

    [Fact]
    public async Task The_ref_cap_and_the_empty_list_are_both_refused()
    {
        var gousto = await FirstSummary("gousto");

        var overCap = Enumerable.Range(0, 15)
            .Select(_ => new ShoppingListRef("gousto", gousto.RecipeId, 2))
            .ToList();

        await ((Func<Task>)(() => Query().ShoppingListAsync(overCap)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*At most 14*");

        await ((Func<Task>)(() => Query().ShoppingListAsync([])))
            .Should().ThrowAsync<ArgumentException>();
    }

    private async Task<RecipeSummary> FirstSummary(string source) =>
        (await Query().SearchAsync(
            new RecipeSearchQuery { Portions = 2, Sources = [source] })).Items[0];

    private RecipeQueryService Query() => fixture.CreateQueryService();
}
