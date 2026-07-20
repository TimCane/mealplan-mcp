using System.Text.Json.Serialization;

namespace Mealplan.Infrastructure.Reading;

/// <summary>
/// A recipe as the MCP surface presents it: the same shape whatever source it
/// came from, so a calling model does not need per-source handling. Allergens
/// holds confirmed contains; TraceAllergens holds may-contain-traces, and is
/// always empty for a source that does not publish the distinction - unknown,
/// not none, per its hasTraceAllergens flag.
/// </summary>
public sealed record RecipeSummary(
    string Source,
    Guid RecipeId,
    string ExternalKey,
    string Name,
    string? Headline,
    int Portions,
    int? PrepMinutes,
    int? TotalMinutes,
    int? Difficulty,
    NutritionPanel Nutrition,
    double? RatingAverage,
    int? RatingCount,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<string> TraceAllergens,
    IReadOnlyList<string> Tags,
    string? ImageUrl);

/// <summary>
/// Per-portion nutrition, whatever the source. A null field is a nutrient the
/// source did not publish for the recipe - never zero.
/// </summary>
public sealed record NutritionPanel(
    double? Kcal,
    double? Kj,
    double? FatGrams,
    double? SaturatesGrams,
    double? CarbsGrams,
    double? SugarsGrams,
    double? FibreGrams,
    double? ProteinGrams,
    double? SaltGrams);

public sealed record RecipeIngredient(
    string Name,
    double? Amount,
    string? Unit);

public sealed record RecipeDetail(
    string Source,
    Guid RecipeId,
    string ExternalKey,
    string Name,
    string? Headline,
    string? Description,
    int Portions,
    IReadOnlyList<int> OfferedPortions,
    int? PrepMinutes,
    int? TotalMinutes,
    int? Difficulty,
    NutritionPanel Nutrition,
    double? ServingSizeGrams,
    double? RatingAverage,
    int? RatingCount,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<string> TraceAllergens,
    IReadOnlyList<string> Tags,
    string? ImageUrl,
    string? WebsiteUrl,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> PantryItems,
    IReadOnlyList<string> Utensils,
    SourceNotes Notes);

/// <summary>
/// What this source could not tell us about this recipe. Present so a null
/// amount reads as "not published" rather than "none needed", and an empty
/// traces list as "unknown" rather than "none".
/// </summary>
public sealed record SourceNotes(
    bool HasIngredientQuantities,
    bool HasPantryItems,
    bool HasTraceAllergens,
    bool HasUtensils,
    string? Caveat);

/// <summary>
/// One page of a list-shaped result. Total always reports the full count, so a
/// caller knows it saw a page and not the world.
/// </summary>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Skip,
    int Take);

public sealed record SourceInfo(
    string Source,
    string DisplayName,
    int RecipeCount,
    bool HasIngredientQuantities,
    bool HasPantryItems,
    bool HasNutrition,
    bool HasTraceAllergens,
    bool HasUtensils,
    IReadOnlyList<int> PortionSizes);

public sealed record ScrapeStatus(
    string Source,
    string? LastRunStatus,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunFinishedAt,
    int StoredDocuments,
    int PendingNormalization,
    string? LastError);

/// <summary>
/// One allergen in a source's vocabulary. Slug is the filter token - it matches
/// the recipe rows' allergen arrays exactly, so a value read here is safe to
/// pass to excludeAllergens. TraceCount is 0 for a source whose
/// hasTraceAllergens flag is false: unknown, not none.
/// </summary>
public sealed record AllergenInfo(
    string Source,
    string Slug,
    string Name,
    int RecipeCount,
    int TraceCount);

public sealed record CuisineInfo(
    string Source,
    string Slug,
    string Name,
    int RecipeCount);

/// <summary>
/// Slug is whatever token the recipe rows' tags array carries for the source -
/// a real slug where one is published, the display title where not.
/// </summary>
public sealed record TagInfo(
    string Source,
    string Slug,
    string Name,
    int RecipeCount);

/// <summary>
/// An ingredient as one source names it. Family is the source's own grouping
/// and null for sources that do not publish one.
/// </summary>
public sealed record IngredientInfo(
    string Source,
    string Name,
    string? Family,
    int RecipeCount);

/// <summary>One recipe at one portion count, as get_shopping_list takes it.</summary>
public sealed record ShoppingListRef(
    string Source,
    Guid RecipeId,
    int Portions);

/// <summary>
/// One line of a shopping list, tagged by the recipe it belongs to so nothing
/// is ambiguous - the server does not merge rows across recipes or sources;
/// the calling model does. A pantry row is an item the box will not contain
/// and the shopper must have at home. A null amount means the source did not
/// publish one - not zero.
/// </summary>
public sealed record ShoppingListRow(
    string RecipeName,
    string Source,
    string Ingredient,
    double? Amount,
    string? Unit,
    bool IsPantryItem);

/// <summary>
/// A nutrient a search can range-filter on. Serialised by the names the tool
/// surface documents, so the wire values stay stable if members are renamed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Nutrient>))]
public enum Nutrient
{
    [JsonStringEnumMemberName("kcal")] Kcal,
    [JsonStringEnumMemberName("kj")] Kj,
    [JsonStringEnumMemberName("fat")] Fat,
    [JsonStringEnumMemberName("saturates")] Saturates,
    [JsonStringEnumMemberName("carbs")] Carbs,
    [JsonStringEnumMemberName("sugars")] Sugars,
    [JsonStringEnumMemberName("fibre")] Fibre,
    [JsonStringEnumMemberName("protein")] Protein,
    [JsonStringEnumMemberName("salt")] Salt,
}

/// <summary>
/// A range on one nutrient, per portion - grams, with kcal and kj in their own
/// units. A recipe with no published value for the nutrient is excluded, the
/// same rule as every other null-bearing filter.
/// </summary>
public sealed record NutrientFilter(
    Nutrient Nutrient,
    double? Min = null,
    double? Max = null);

[JsonConverter(typeof(JsonStringEnumConverter<RecipeSort>))]
public enum RecipeSort
{
    [JsonStringEnumMemberName("name")] Name,
    [JsonStringEnumMemberName("rating")] Rating,
    [JsonStringEnumMemberName("kcal")] Kcal,
    [JsonStringEnumMemberName("random")] Random,
}

/// <summary>Filters for a recipe search. All optional; all combine with AND.</summary>
public sealed record RecipeSearchQuery
{
    public string? Query { get; init; }

    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>Portion count to price the recipe at. Defaults to 2.</summary>
    public int Portions { get; init; } = 2;

    public int? MaxPrepMinutes { get; init; }

    /// <summary>Same null-excludes rule as <see cref="MaxPrepMinutes"/>.</summary>
    public int? MaxTotalMinutes { get; init; }

    public IReadOnlyList<string>? Cuisines { get; init; }

    /// <summary>Tag slugs as list_tags reports them. Matches any.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Recipes carrying any of these allergens are excluded.</summary>
    public IReadOnlyList<string>? ExcludeAllergens { get; init; }

    /// <summary>
    /// Whether <see cref="ExcludeAllergens"/> also matches may-contain-traces.
    /// Over-excluding is the only safe default in an allergen-bearing dataset;
    /// false narrows to confirmed contains only.
    /// </summary>
    public bool ExcludeTraces { get; init; } = true;

    /// <summary>Recipes must contain every one of these ingredients.</summary>
    public IReadOnlyList<string>? IncludeIngredients { get; init; }

    /// <summary>
    /// Dislikes: recipes containing any of these ingredients are excluded, on
    /// the same substring semantics as <see cref="IncludeIngredients"/>.
    /// Over-excluding is the right direction for dislikes as it is for
    /// allergens.
    /// </summary>
    public IReadOnlyList<string>? ExcludeIngredients { get; init; }

    public IReadOnlyList<NutrientFilter>? NutrientFilters { get; init; }

    /// <summary>Recipes with no rating are excluded when set.</summary>
    public double? MinRating { get; init; }

    public RecipeSort Sort { get; init; } = RecipeSort.Name;

    /// <summary>
    /// Fixes the random sort so paging is stable within a seed and a new seed
    /// reshuffles. Ignored for the other sorts.
    /// </summary>
    public int? Seed { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 20;
}

/// <summary>
/// Thrown when the recipe exists but is not offered at the requested portion
/// count, carrying the counts that would work - so the caller learns the fix
/// from the failure instead of a bare not-found.
/// </summary>
public sealed class PortionsNotOfferedException(
    string source,
    Guid recipeId,
    int requested,
    IReadOnlyList<int> offered)
    : Exception(
        $"Recipe {recipeId} from {source} is not offered at {requested} portions. "
        + $"Offered portion counts: {string.Join(", ", offered)}.")
{
    public IReadOnlyList<int> OfferedPortions { get; } = offered;
}
