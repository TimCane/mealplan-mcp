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
public class RecipeTools(RecipeQueryService service)
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
        [Description("Exclude recipes whose total time exceeds this. Recipes with no published total time are excluded.")]
        int? maxTotalMinutes = null,
        [Description("Cuisine slugs, e.g. [\"italian\"]. Matches any. Resolve with list_cuisines.")]
        string[]? cuisines = null,
        [Description("Tag slugs, e.g. [\"pasta-noodles\"]. Matches any. Resolve with list_tags - "
            + "tokens differ per source.")]
        string[]? tags = null,
        [Description("Allergen slugs to avoid, e.g. [\"milk\",\"gluten\"]. Any match excludes "
            + "the recipe, in confirmed contains and - by default - may-contain-traces alike.")]
        string[]? excludeAllergens = null,
        [Description("Whether excludeAllergens also matches may-contain-traces. Defaults to "
            + "true, the safe direction; false narrows to confirmed contains only. Sources "
            + "with hasTraceAllergens false publish no traces data, so their recipes are "
            + "never excluded on traces either way.")]
        bool excludeTraces = true,
        [Description("Ingredient names that must all appear, e.g. [\"chicken\"]. Substring match.")]
        string[]? includeIngredients = null,
        [Description("Dislikes, e.g. [\"mushroom\"]. Any substring match excludes the recipe - "
            + "over-excluding is the right direction for dislikes as it is for allergens.")]
        string[]? excludeIngredients = null,
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
        return await service.SearchAsync(
            new RecipeSearchQuery
            {
                Query = query,
                Sources = sources,
                Portions = portions,
                MaxPrepMinutes = maxPrepMinutes,
                MaxTotalMinutes = maxTotalMinutes,
                Cuisines = cuisines,
                Tags = tags,
                ExcludeAllergens = excludeAllergens,
                ExcludeTraces = excludeTraces,
                IncludeIngredients = includeIngredients,
                ExcludeIngredients = excludeIngredients,
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
        "Full detail for one recipe: ingredients, method, utensils, allergens and "
        + "per-portion nutrition, at the requested portion count. Allergens are "
        + "confirmed contains; traceAllergens are may-contain-traces, and are empty "
        + "with a caveat for sources that do not publish the distinction - unknown, "
        + "not none. The notes field reports what the source does not publish, so "
        + "an absent amount is not mistaken for none needed. Asking for a portion "
        + "count the recipe is not offered at fails with the counts that would work.")]
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
            return await service.GetAsync(source, recipeId, portions, ct);
        }
        catch (PortionsNotOfferedException ex)
        {
            // Surfaced as a protocol error rather than a null: the id was right,
            // and the message carries the portion counts that would succeed.
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(
        Name = "list_allergens",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Every allergen slug and display name per source, with counts of recipes "
        + "confirmed to contain it and recipes that may contain traces. The "
        + "authoritative input for excludeAllergens - check slugs here before "
        + "filtering, because a misspelt slug silently matches nothing. "
        + "traceCount is 0 for sources whose hasTraceAllergens flag is false: "
        + "unknown, not none. Ordered by recipe count; total reports the full "
        + "count, though page one is normally the whole list.")]
    public async Task<Page<AllergenInfo>> ListAllergensAsync(
        [Description("Restrict to these source slugs. Omit for all.")]
        string[]? sources = null,
        [Description("Rows to skip, for paging.")] int skip = 0,
        [Description("Rows to return, 1 to 100. Defaults to 50.")] int take = 50,
        CancellationToken ct = default)
    {
        return await service.ListAllergensAsync(sources, skip, take, ct);
    }

    [McpServerTool(
        Name = "list_cuisines",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Cuisine slugs and display names per source with recipe counts, ordered "
        + "by count. The authoritative input for the cuisines search filter. "
        + "total reports the full count - page if it exceeds the rows returned.")]
    public async Task<Page<CuisineInfo>> ListCuisinesAsync(
        [Description("Restrict to these source slugs. Omit for all.")]
        string[]? sources = null,
        [Description("Rows to skip, for paging.")] int skip = 0,
        [Description("Rows to return, 1 to 100. Defaults to 50.")] int take = 50,
        CancellationToken ct = default)
    {
        return await service.ListCuisinesAsync(sources, skip, take, ct);
    }

    [McpServerTool(
        Name = "list_tags",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Tag slugs and display names per source with recipe counts, ordered by "
        + "count. The authoritative input for the tags search filter; tokens "
        + "differ per source, so filter with the slugs listed here rather than "
        + "guessed ones. total reports the full count - tag vocabularies can run "
        + "to hundreds of rows, so page one is not the world.")]
    public async Task<Page<TagInfo>> ListTagsAsync(
        [Description("Restrict to these source slugs. Omit for all.")]
        string[]? sources = null,
        [Description("Rows to skip, for paging.")] int skip = 0,
        [Description("Rows to return, 1 to 100. Defaults to 50.")] int take = 50,
        CancellationToken ct = default)
    {
        return await service.ListTagsAsync(sources, skip, take, ct);
    }

    [McpServerTool(
        Name = "search_ingredients",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "The ingredient catalogue as each source names it, filtered by free "
        + "text against name and family. Use it to resolve a household name to "
        + "what a source actually calls it before relying on includeIngredients. "
        + "family is the source's own grouping and null "
        + "where none is published. Ordered by recipe count; total reports the "
        + "full match count.")]
    public async Task<Page<IngredientInfo>> SearchIngredientsAsync(
        [Description("Substring matched against ingredient name and family, e.g. \"garlic\". Omit to list all.")]
        string? query = null,
        [Description("Restrict to these source slugs. Omit for all.")]
        string[]? sources = null,
        [Description("Rows to skip, for paging.")] int skip = 0,
        [Description("Rows to return, 1 to 100. Defaults to 50.")] int take = 50,
        CancellationToken ct = default)
    {
        return await service.SearchIngredientsAsync(query, sources, skip, take, ct);
    }

    [McpServerTool(
        Name = "get_shopping_list",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Flat shopping rows for up to 14 chosen recipes at their chosen portion "
        + "counts: one row per ingredient, tagged by recipe and source. Rows "
        + "with isPantryItem true are staples the box will not contain and the "
        + "shopper must have at home. Nothing is merged across recipes or "
        + "sources - identical ingredients appear once per recipe, and the "
        + "caller does the combining. A null amount means the source publishes "
        + "no quantities (see hasIngredientQuantities), not zero. A ref with an "
        + "unknown id or an unoffered portion count fails the whole call, "
        + "naming the problem.")]
    public async Task<IReadOnlyList<ShoppingListRow>> GetShoppingListAsync(
        [Description("Recipes to shop for, e.g. [{\"source\":\"gousto\",\"recipeId\":\"...\",\"portions\":2}]. "
            + "Up to 14; portions must be a count the recipe is offered at.")]
        ShoppingListRef[] recipes,
        CancellationToken ct = default)
    {
        try
        {
            return await service.ShoppingListAsync(recipes, ct);
        }
        catch (Exception ex) when (
            ex is PortionsNotOfferedException or KeyNotFoundException or ArgumentException)
        {
            // One bad ref fails the whole list - a plan silently missing a
            // night's ingredients is worse than an error naming the fix.
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
        return await service.ListSourcesAsync(ct);
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
        return await service.ScrapeStatusAsync(ct);
    }
}
