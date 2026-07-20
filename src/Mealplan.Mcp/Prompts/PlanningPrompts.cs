using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Mealplan.Mcp.Prompts;

/// <summary>
/// Parameterised flow templates, surfaced as slash-commands in MCP clients.
/// These are the documentation of record for the safe flow: allergen slug
/// resolution before filtering, traces semantics, portion validity. Each
/// renders to one user message steering the calling model through the tools.
/// </summary>
[McpServerPromptType]
public class PlanningPrompts
{
    [McpServerPrompt(Name = "plan_week")]
    [Description(
        "Plan a week of dinners: resolve constraints to real slugs, search with "
        + "a seeded shuffle, and finish with a shopping list.")]
    public static string PlanWeek(
        [Description("How many people each meal feeds. Defaults to 2.")]
        string? people = null,
        [Description("How many dinners to plan. Defaults to 5.")]
        string? nights = null,
        [Description("Allergens to exclude, comma separated, e.g. \"milk, gluten\".")]
        string? allergens = null,
        [Description("Disliked ingredients, comma separated, e.g. \"mushroom, olives\".")]
        string? dislikes = null,
        [Description("Per-portion kcal ceiling per dinner.")]
        string? maxKcal = null,
        [Description("Per-portion protein floor in grams per dinner.")]
        string? minProtein = null,
        [Description("Restrict to these source slugs, comma separated.")]
        string? sources = null)
    {
        var template = new StringBuilder();

        template.Append(
            $"Plan {nights ?? "5"} dinners for {people ?? "2"} people using the "
            + "mealplan tools, then produce a shopping list. Follow this flow:\n"
            + "\n"
            + "1. Call list_sources first. Note each source's capability flags: "
            + "hasIngredientQuantities false means amounts are never published "
            + "(not zero), and hasTraceAllergens false means an empty "
            + "traceAllergens list is unknown, not none.\n");

        template.Append(allergens is null
            ? "2. Ask whether anyone has allergies before searching - do not assume none.\n"
            : $"2. Allergens to exclude: {allergens}. Resolve each to per-source "
              + "slugs with list_allergens before filtering - a guessed slug "
              + "silently matches nothing. Pass the resolved slugs as "
              + "excludeAllergens and leave excludeTraces true: may-contain-traces "
              + "counts as unsafe unless the user explicitly relaxes it.\n");

        template.Append(
            $"3. Search with search_recipes at portions={people ?? "2"}, "
            + "sort=random with the current ISO week number as seed, so this "
            + "week's plan draws from a fresh shuffle and paging stays stable");

        if (sources is not null)
        {
            template.Append($", restricted to sources: {sources}");
        }

        var nutrients = new List<string>();

        if (maxKcal is not null)
        {
            nutrients.Add($"{{\"nutrient\":\"kcal\",\"max\":{maxKcal}}}");
        }

        if (minProtein is not null)
        {
            nutrients.Add($"{{\"nutrient\":\"protein\",\"min\":{minProtein}}}");
        }

        if (nutrients.Count > 0)
        {
            template.Append(
                $", with nutrientFilters=[{string.Join(",", nutrients)}] - recipes "
                + "with no published value for a filtered nutrient are excluded");
        }

        template.Append(
            ".\n");

        if (dislikes is not null)
        {
            template.Append(
                $"4. Dislikes: {dislikes}. Resolve what each source calls them "
                + "with search_ingredients, then pass them as excludeIngredients - "
                + "any match excludes, which is the right direction for dislikes.\n");
        }

        template.Append(
            $"{(dislikes is null ? "4" : "5")}. Pick {nights ?? "5"} recipes the "
            + "user will like, varying cuisine across the week. Results are one "
            + "page, not the world - page further if the first page is not "
            + "enough. Present the picks with per-portion kcal and protein and "
            + "wait for approval.\n"
            + $"{(dislikes is null ? "5" : "6")}. Finish with get_shopping_list "
            + "for the approved recipes at the agreed portion counts. Rows with "
            + "isPantryItem true are staples the box will not contain - list "
            + "them separately as a check-your-cupboard section. Merge duplicate "
            + "ingredients across recipes yourself; the server deliberately "
            + "does not.");

        return template.ToString();
    }

    [McpServerPrompt(Name = "find_recipe")]
    [Description("Find one recipe for a craving, honouring any constraints.")]
    public static string FindRecipe(
        [Description("What the user fancies, in their own words, e.g. \"something with lemongrass\".")]
        string craving,
        [Description("Constraints in plain words, e.g. \"no nuts, under 600 kcal, quick\".")]
        string? constraints = null,
        [Description("Cuisine to stay within, e.g. \"italian\".")]
        string? cuisine = null)
    {
        var template = new StringBuilder();

        template.Append(
            $"Find one recipe for: {craving}.\n"
            + "\n"
            + "Search with search_recipes using the craving as the free-text "
            + "query - it matches ingredient names too, so \"lemongrass\" finds "
            + "recipes where it is only an ingredient.\n");

        if (constraints is not null)
        {
            template.Append(
                $"Constraints: {constraints}. Map them onto filters, resolving "
                + "slugs first: allergens via list_allergens (passed as "
                + "excludeAllergens, traces excluded by default), dislikes via "
                + "search_ingredients into excludeIngredients, time limits onto "
                + "maxPrepMinutes or maxTotalMinutes, and nutrient limits onto "
                + "nutrientFilters. Filters exclude recipes missing the filtered "
                + "value rather than guessing.\n");
        }

        if (cuisine is not null)
        {
            template.Append(
                $"Stay within this cuisine: {cuisine}. Resolve the slug per "
                + "source with list_cuisines before filtering.\n");
        }

        template.Append(
            "Offer the best two or three matches with a line on why, then call "
            + "get_recipe for the chosen one and present it: ingredients, "
            + "method, and the notes field's caveats about what the source does "
            + "not publish.");

        return template.ToString();
    }

    [McpServerPrompt(Name = "whats_available")]
    [Description("Orient in the catalogue: sources, freshness, cuisines and tags.")]
    public static string WhatsAvailable() =>
        "Give the user a short orientation in the recipe catalogue:\n"
        + "\n"
        + "1. list_sources - how many recipes each source holds and what each "
        + "cannot say (no quantities from Gousto, no traces data either: those "
        + "fields read unknown, not none or zero).\n"
        + "2. get_scrape_status - how fresh the data is; mention it only if a "
        + "source looks stale or empty.\n"
        + "3. list_cuisines and list_tags - the busiest cuisines and tags per "
        + "source. Both are paged and count-ordered, so page one is the "
        + "highlight reel, and total reports how much more there is.\n"
        + "\n"
        + "Close with what the user can ask for next: searching by craving, "
        + "filtering by allergens or nutrition, or planning a week.";
}
