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
    /// does separate store-cupboard basics from what it sends. It publishes no
    /// may-contain-traces data and no utensils, so both read as unknown rather
    /// than none.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: false,
        HasPantryItems: true,
        HasNutrition: true,
        HasTraceAllergens: false,
        HasUtensils: false,
        PortionSizes: [1, 2, 3, 4, 5]);

    /// <summary>
    /// One row per recipe per offered portion count. Prep time comes from the
    /// yield, so it is null for portion counts Gousto does not publish one for.
    /// Nutrition selects the basis = per_portion row, matching the view's
    /// per-portion contract; the per-100g row stays unsurfaced.
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
            n.energy_kj                   AS energy_kj,
            n.fat_grams                   AS fat_g,
            n.saturated_fat_grams         AS saturates_g,
            n.carbs_grams                 AS carbs_g,
            n.sugars_grams                AS sugars_g,
            n.fibre_grams                 AS fibre_g,
            n.protein_grams               AS protein_g,
            n.salt_grams                  AS salt_g,
            n.net_weight_grams            AS serving_size_g,
            r.rating_average              AS rating_avg,
            r.rating_count                AS rating_count,
            CASE WHEN c.slug IS NULL THEN ARRAY[]::text[] ELSE ARRAY[c.slug::text] END AS cuisines,
            COALESCE(a.slugs, ARRAY[]::text[])  AS allergens,
            ARRAY[]::text[]                     AS trace_allergens,
            COALESCE(g.titles, ARRAY[]::text[]) AS tags,
            r.image_url                   AS image_url,
            r.website_url                 AS website_url,
            concat_ws(' ', r.title, r.description, ing.names) AS search_text
        FROM gousto.recipe r
        JOIN gousto.recipe_yield y ON y.recipe_id = r.id
        LEFT JOIN gousto.cuisine c ON c.id = r.cuisine_id
        LEFT JOIN gousto.recipe_nutrition n
            ON n.recipe_id = r.id AND n.basis = 1
        LEFT JOIN LATERAL (
            SELECT string_agg(DISTINCT i.name, ' ') AS names
            FROM gousto.recipe_yield y2
            JOIN gousto.recipe_yield_ingredient yi ON yi.yield_id = y2.id
            JOIN gousto.ingredient i ON i.id = yi.ingredient_id
            WHERE y2.recipe_id = r.id
        ) ing ON true
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
