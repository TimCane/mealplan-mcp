using System.ComponentModel;
using Mealplan.Infrastructure.Reading;
using ModelContextProtocol.Server;

namespace Mealplan.Mcp.Tools;

/// <summary>
/// The read surface. Meal plans are not stored here - the calling model holds
/// the plan; this server answers questions about recipes.
/// </summary>
[McpServerToolType]
public class RecipeTools(RecipeQueryService recipes)
{
    [McpServerTool(Name = "search_recipes")]
    [Description(
        "Search recipes across every configured meal-kit source. All filters are "
        + "optional and combine with AND. Returns a page of summaries; call "
        + "get_recipe for ingredients and method.")]
    public async Task<SearchResult> SearchRecipesAsync(
        [Description("Free text matched against name, headline and description. Tolerates typos.")]
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
        [Description("Maximum calories per portion. Recipes with no published value are excluded.")]
        double? maxKcal = null,
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
                MaxKcal = maxKcal,
                Skip = skip,
                Take = take,
            },
            ct);
    }

    [McpServerTool(Name = "get_recipe")]
    [Description(
        "Full detail for one recipe: ingredients, method, allergens and nutrition, "
        + "at the requested portion count. The notes field reports what the source "
        + "does not publish, so an absent amount is not mistaken for none needed.")]
    public async Task<RecipeDetail?> GetRecipeAsync(
        [Description("Source slug, as returned by search_recipes or list_sources.")]
        string source,
        [Description("Recipe id, as returned by search_recipes.")] Guid recipeId,
        [Description("Portion count. Must be one the source offers; see list_sources.")]
        int portions = 2,
        CancellationToken ct = default)
    {
        return await recipes.GetAsync(source, recipeId, portions, ct);
    }

    [McpServerTool(Name = "list_sources")]
    [Description(
        "The configured recipe sources, how many recipes each holds, and what each "
        + "can and cannot report. Check hasIngredientQuantities before relying on "
        + "amounts: Gousto ships boxes and publishes none.")]
    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(CancellationToken ct = default)
    {
        return await recipes.ListSourcesAsync(ct);
    }

    [McpServerTool(Name = "get_scrape_status")]
    [Description(
        "Freshness of the recipe data: the last crawl per source and how much is "
        + "still waiting to be normalised. Useful when results look thin.")]
    public async Task<IReadOnlyList<ScrapeStatus>> GetScrapeStatusAsync(CancellationToken ct = default)
    {
        return await recipes.ScrapeStatusAsync(ct);
    }
}
