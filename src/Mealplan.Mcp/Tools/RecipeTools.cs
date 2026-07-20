using System.ComponentModel;
using Mealplan.Infrastructure.Reading;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Mealplan.Mcp.Tools;

/// <summary>
/// The read surface. Meal plans are not stored here - the calling model holds
/// the plan; this server answers questions about recipes.
/// </summary>
/// <remarks>
/// Every tool is annotated read-only, idempotent and closed-world: the server
/// cannot write, so clients may auto-approve calls instead of prompting for
/// each one. Structured output returns typed results with a schema generated
/// from the records, which also encodes which fields are nullable.
/// </remarks>
[McpServerToolType]
public class RecipeTools(RecipeQueryService recipes)
{
    [McpServerTool(
        Name = "search_recipes",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Search recipes across every configured meal-kit source. All filters are "
        + "optional and combine with AND. Filters on a value a source does not "
        + "publish (prep time, a nutrient, a rating) exclude its recipes rather "
        + "than guessing. Returns one page; total reports the full match count, "
        + "which can exceed the rows returned. Call get_recipe for ingredients "
        + "and method.")]
    public async Task<Page<RecipeSummary>> SearchRecipesAsync(
        [Description("Free text matched against name, headline, description and ingredient names. Tolerates typos.")]
        string? query = null,
        [Description("Restrict to these source slugs, e.g. [\"gousto\"]. Omit to search all.")]
        string[]? sources = null,
        [Description("Portion count to report the recipe at. Defaults to 2.")]
        int portions = 2,
        [Description("Exclude recipes needing longer than this to prepare. Recipes with no published prep time are excluded.")]
        int? maxPrepMinutes = null,
        [Description("Cuisine slugs, e.g. [\"italian\"]. Matches any.")]
        string[]? cuisines = null,
        [Description("Allergen slugs to avoid, e.g. [\"milk\",\"gluten\"]. Any match excludes the recipe.")]
        string[]? excludeAllergens = null,
        [Description("Ingredient names that must all appear, e.g. [\"chicken\"]. Substring match.")]
        string[]? includeIngredients = null,
        [Description("Per-portion nutrient ranges, e.g. [{\"nutrient\":\"protein\",\"min\":30}]. "
            + "Grams per portion; kcal and kj in their own units. Recipes with no "
            + "published value for a filtered nutrient are excluded.")]
        NutrientFilter[]? nutrientFilters = null,
        [Description("Minimum average rating, 0 to 5. Unrated recipes are excluded when set.")]
        double? minRating = null,
        [Description("Row order: name (default), rating (best first), kcal (lightest first), "
            + "or random. Random with a seed pages stably; a new seed reshuffles.")]
        RecipeSort sort = RecipeSort.Name,
        [Description("Fixes the random sort, e.g. an ISO week number. Ignored for other sorts.")]
        int? seed = null,
        [Description("Rows to skip, for paging.")] int skip = 0,
        [Description("Rows to return, 1 to 100. Defaults to 20.")] int take = 20,
        CancellationToken ct = default)
    {
        return await recipes.SearchAsync(
            new RecipeSearchQuery
            {
                Query = query,
                Sources = sources,
                Portions = portions,
                MaxPrepMinutes = maxPrepMinutes,
                Cuisines = cuisines,
                ExcludeAllergens = excludeAllergens,
                IncludeIngredients = includeIngredients,
                NutrientFilters = nutrientFilters,
                MinRating = minRating,
                Sort = sort,
                Seed = seed,
                Skip = skip,
                Take = take,
            },
            ct);
    }

    [McpServerTool(
        Name = "get_recipe",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Full detail for one recipe: ingredients, method, allergens and per-portion "
        + "nutrition, at the requested portion count. The notes field reports what "
        + "the source does not publish, so an absent amount is not mistaken for "
        + "none needed. Asking for a portion count the recipe is not offered at "
        + "fails with the counts that would work.")]
    public async Task<RecipeDetail?> GetRecipeAsync(
        [Description("Source slug, as returned by search_recipes or list_sources.")]
        string source,
        [Description("Recipe id, as returned by search_recipes.")] Guid recipeId,
        [Description("Portion count. Must be one the recipe is offered at; offeredPortions on the detail lists them.")]
        int portions = 2,
        CancellationToken ct = default)
    {
        try
        {
            return await recipes.GetAsync(source, recipeId, portions, ct);
        }
        catch (PortionsNotOfferedException ex)
        {
            // Surfaced as a protocol error rather than a null: the id was right,
            // and the message carries the portion counts that would succeed.
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(
        Name = "list_sources",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "The configured recipe sources, how many recipes each holds, and what each "
        + "can and cannot report. Check hasIngredientQuantities before relying on "
        + "amounts: Gousto ships boxes and publishes none.")]
    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(CancellationToken ct = default)
    {
        return await recipes.ListSourcesAsync(ct);
    }

    [McpServerTool(
        Name = "get_scrape_status",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Freshness of the recipe data: the last crawl per source and how much is "
        + "still waiting to be normalised. Useful when results look thin.")]
    public async Task<IReadOnlyList<ScrapeStatus>> GetScrapeStatusAsync(CancellationToken ct = default)
    {
        return await recipes.ScrapeStatusAsync(ct);
    }
}
