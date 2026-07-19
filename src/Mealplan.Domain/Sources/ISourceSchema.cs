namespace Mealplan.Domain.Sources;

/// <summary>
/// Where a source's normalised data lives and what it can offer. Keeps schema
/// naming and capability reporting next to the source that owns them rather than
/// in a table the whole system has to agree on.
/// </summary>
public interface ISourceSchema
{
    string Source { get; }

    /// <summary>Human-readable name for the MCP surface, e.g. "HelloFresh".</summary>
    string DisplayName { get; }

    /// <summary>Postgres schema holding this source's normalised tables.</summary>
    string SchemaName { get; }

    SourceCapabilities Capabilities { get; }

    /// <summary>
    /// This source's contribution to the cross-source read views: a SELECT whose
    /// columns match the contract in <see cref="RecipeViewColumns"/> exactly, in
    /// that order. The views are rebuilt from every registered source, so adding
    /// a source needs no edit to a view definition somewhere else.
    /// </summary>
    string RecipeViewSql { get; }

    /// <summary>
    /// As <see cref="RecipeViewSql"/>, for ingredients. A source with no
    /// quantities selects NULL for amount and unit rather than inventing them.
    /// </summary>
    string RecipeIngredientViewSql { get; }
}

/// <summary>
/// The column contract every source's projection must satisfy. Named here so a
/// source can be checked against it rather than discovering a mismatch when
/// Postgres refuses the UNION.
/// </summary>
public static class RecipeViewColumns
{
    public static IReadOnlyList<string> Recipe { get; } =
    [
        "source",
        "recipe_id",
        "external_key",
        "name",
        "headline",
        "description",
        "portions",
        "prep_minutes",
        "total_minutes",
        "difficulty",
        "kcal",
        "cuisines",
        "allergens",
        "tags",
        "image_url",
        "search_text",
    ];

    public static IReadOnlyList<string> RecipeIngredient { get; } =
    [
        "source",
        "recipe_id",
        "portions",
        "ingredient_name",
        "amount",
        "unit",
    ];
}
