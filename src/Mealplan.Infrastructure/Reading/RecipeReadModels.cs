namespace Mealplan.Infrastructure.Reading;

/// <summary>
/// A recipe as the MCP surface presents it: the same shape whatever source it
/// came from, so a calling model does not need per-source handling.
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
    double? Kcal,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<string> Tags,
    string? ImageUrl);

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
    int? PrepMinutes,
    int? TotalMinutes,
    int? Difficulty,
    double? Kcal,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<string> Tags,
    string? ImageUrl,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> PantryItems,
    SourceNotes Notes);

/// <summary>
/// What this source could not tell us about this recipe. Present so a null
/// amount reads as "not published" rather than "none needed".
/// </summary>
public sealed record SourceNotes(
    bool HasIngredientQuantities,
    bool HasPantryItems,
    string? Caveat);

public sealed record SearchResult(
    IReadOnlyList<RecipeSummary> Recipes,
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
    IReadOnlyList<int> PortionSizes);

public sealed record ScrapeStatus(
    string Source,
    string? LastRunStatus,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunFinishedAt,
    int StoredDocuments,
    int PendingNormalization,
    string? LastError);

/// <summary>Filters for a recipe search. All optional; all combine with AND.</summary>
public sealed record RecipeSearchQuery
{
    public string? Query { get; init; }

    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>Portion count to price the recipe at. Defaults to 2.</summary>
    public int Portions { get; init; } = 2;

    public int? MaxPrepMinutes { get; init; }

    public IReadOnlyList<string>? Cuisines { get; init; }

    /// <summary>Recipes carrying any of these allergens are excluded.</summary>
    public IReadOnlyList<string>? ExcludeAllergens { get; init; }

    /// <summary>Recipes must contain every one of these ingredients.</summary>
    public IReadOnlyList<string>? IncludeIngredients { get; init; }

    public double? MaxKcal { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 20;
}
