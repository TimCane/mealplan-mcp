using FluentAssertions;
using Mealplan.Mcp.Prompts;

namespace Mealplan.IntegrationTests;

/// <summary>
/// The prompts are the documentation of record for the safe flow, so the
/// safety-bearing lines are pinned across the argument combinations.
/// </summary>
public class PlanningPromptTests
{
    [Fact]
    public void Plan_week_defaults_ask_about_allergies_rather_than_assuming_none()
    {
        var rendered = PlanningPrompts.PlanWeek();

        rendered.Should().Contain("5 dinners for 2 people")
            .And.Contain("list_sources")
            .And.Contain("Ask whether anyone has allergies")
            .And.Contain("ISO week number as seed")
            .And.Contain("get_shopping_list");
    }

    [Fact]
    public void Plan_week_with_every_argument_encodes_the_full_safe_flow()
    {
        var rendered = PlanningPrompts.PlanWeek(
            people: "4",
            nights: "3",
            allergens: "milk, gluten",
            dislikes: "mushroom",
            maxKcal: "700",
            minProtein: "30",
            sources: "gousto");

        rendered.Should().Contain("3 dinners for 4 people")
            .And.Contain("milk, gluten")
            .And.Contain("list_allergens", "slugs must be resolved before filtering")
            .And.Contain("excludeTraces true", "the traces default is the safety line")
            .And.Contain("search_ingredients").And.Contain("excludeIngredients")
            .And.Contain("{\"nutrient\":\"kcal\",\"max\":700}")
            .And.Contain("{\"nutrient\":\"protein\",\"min\":30}")
            .And.Contain("sources: gousto")
            .And.NotContain("Ask whether anyone has allergies");
    }

    [Fact]
    public void Find_recipe_searches_the_craving_and_ends_in_get_recipe()
    {
        var bare = PlanningPrompts.FindRecipe("something with lemongrass");

        bare.Should().Contain("something with lemongrass")
            .And.Contain("search_recipes")
            .And.Contain("get_recipe")
            .And.NotContain("Constraints:");

        var constrained = PlanningPrompts.FindRecipe(
            "comfort food",
            constraints: "no nuts, under 600 kcal",
            cuisine: "italian");

        constrained.Should().Contain("no nuts, under 600 kcal")
            .And.Contain("list_allergens")
            .And.Contain("nutrientFilters")
            .And.Contain("list_cuisines", "cuisine slugs are per source");
    }

    [Fact]
    public void Whats_available_walks_the_orientation_tools()
    {
        var rendered = PlanningPrompts.WhatsAvailable();

        rendered.Should().Contain("list_sources")
            .And.Contain("get_scrape_status")
            .And.Contain("list_cuisines")
            .And.Contain("list_tags")
            .And.Contain("unknown, not none");
    }
}
