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

    /// <summary>
    /// The source's allergen vocabulary with usage counts: slugs as they appear
    /// in v_recipe's arrays, so a value read here is guaranteed to work as an
    /// exclusion filter. A source that publishes no traces data selects 0 for
    /// trace_count - unknown, not none, per its capability flag.
    /// </summary>
    string AllergenViewSql { get; }

    /// <summary>As <see cref="AllergenViewSql"/>, for cuisines.</summary>
    string CuisineViewSql { get; }

    /// <summary>
    /// As <see cref="AllergenViewSql"/>, for tags. The slug column carries
    /// whatever token v_recipe's tags array holds for this source, even where
    /// the source publishes no real slug.
    /// </summary>
    string TagViewSql { get; }

    /// <summary>
    /// The source's ingredient catalogue by name: what each source actually
    /// calls things, so a caller can resolve "garlic" before filtering on it.
    /// Family is NULL for sources that do not group ingredients.
    /// </summary>
    string IngredientViewSql { get; }
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
        "energy_kj",
        "fat_g",
        "saturates_g",
        "carbs_g",
        "sugars_g",
        "fibre_g",
        "protein_g",
        "salt_g",
        "serving_size_g",
        "rating_avg",
        "rating_count",
        "cuisines",
        "allergens",
        "trace_allergens",
        "tags",
        "image_url",
        "website_url",
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

    public static IReadOnlyList<string> Allergen { get; } =
    [
        "source",
        "slug",
        "name",
        "recipe_count",
        "trace_count",
    ];

    public static IReadOnlyList<string> Cuisine { get; } =
    [
        "source",
        "slug",
        "name",
        "recipe_count",
    ];

    public static IReadOnlyList<string> Tag { get; } =
    [
        "source",
        "slug",
        "name",
        "recipe_count",
    ];

    public static IReadOnlyList<string> Ingredient { get; } =
    [
        "source",
        "name",
        "family",
        "recipe_count",
    ];
}
