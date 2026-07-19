using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Gousto.Persistence;

namespace Mealplan.Infrastructure.Gousto;

public class GoustoSchema : ISourceSchema
{
    public const string SourceSlug = "gousto";

    public string Source => SourceSlug;

    public string DisplayName => "Gousto";

    public string SchemaName => GoustoDbContext.SchemaName;

    /// <summary>
    /// Gousto ships boxes, not measured ingredients: there is no amount or unit
    /// anywhere in the payload, only SKU codes and how many go in the box. It
    /// does separate store-cupboard basics from what it sends.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: false,
        HasPantryItems: true,
        HasNutrition: true,
        PortionSizes: [1, 2, 3, 4, 5]);

    /// <summary>
    /// One row per recipe per offered portion count. Prep time comes from the
    /// yield, so it is null for portion counts Gousto does not publish one for.
    /// </summary>
    public string RecipeViewSql => """
        SELECT
            'gousto'                      AS source,
            r.id                          AS recipe_id,
            r.slug                        AS external_key,
            r.title                       AS name,
            NULL::text                    AS headline,
            r.description                 AS description,
            y.portions                    AS portions,
            y.prep_minutes                AS prep_minutes,
            y.prep_minutes                AS total_minutes,
            NULL::integer                 AS difficulty,
            n.energy_kcal                 AS kcal,
            CASE WHEN c.slug IS NULL THEN ARRAY[]::text[] ELSE ARRAY[c.slug::text] END AS cuisines,
            COALESCE(a.slugs, ARRAY[]::text[])  AS allergens,
            COALESCE(g.titles, ARRAY[]::text[]) AS tags,
            r.image_url                   AS image_url,
            concat_ws(' ', r.title, r.description) AS search_text
        FROM gousto.recipe r
        JOIN gousto.recipe_yield y ON y.recipe_id = r.id
        LEFT JOIN gousto.cuisine c ON c.id = r.cuisine_id
        LEFT JOIN gousto.recipe_nutrition n
            ON n.recipe_id = r.id AND n.basis = 1
        LEFT JOIN LATERAL (
            SELECT array_agg(al.slug::text ORDER BY al.slug) AS slugs
            FROM gousto.recipe_allergen ra
            JOIN gousto.allergen al ON al.id = ra.allergen_id
            WHERE ra.recipe_id = r.id
        ) a ON true
        LEFT JOIN LATERAL (
            SELECT array_agg(cat.title::text ORDER BY cat.title) AS titles
            FROM gousto.recipe_category rc
            JOIN gousto.category cat ON cat.id = rc.category_id
            WHERE rc.recipe_id = r.id
        ) g ON true
        """;

    /// <summary>
    /// Amount and unit are null throughout: Gousto ships boxes and publishes no
    /// quantities. The capability flags say so, so a caller can tell this apart
    /// from a missing value.
    /// </summary>
    public string RecipeIngredientViewSql => """
        SELECT
            'gousto'                AS source,
            y.recipe_id             AS recipe_id,
            y.portions              AS portions,
            i.name                  AS ingredient_name,
            NULL::double precision  AS amount,
            NULL::text              AS unit
        FROM gousto.recipe_yield_ingredient yi
        JOIN gousto.recipe_yield y ON y.id = yi.yield_id
        JOIN gousto.ingredient i ON i.id = yi.ingredient_id
        """;
}
