using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.HelloFresh.Persistence;

namespace Mealplan.Infrastructure.HelloFresh;

public class HelloFreshSchema : ISourceSchema
{
    public const string SourceSlug = "hellofresh";

    /// <summary>
    /// The token is read from the website, so it cannot use the API client that
    /// needs the token. Separate client, separate base address.
    /// </summary>
    public const string TokenClientName = "hellofresh-token";

    public string Source => SourceSlug;

    public string DisplayName => "HelloFresh";

    public string SchemaName => HelloFreshDbContext.SchemaName;

    /// <summary>
    /// HelloFresh publishes measured ingredients per portion count, which Gousto
    /// does not. It does not separate store-cupboard items - everything listed
    /// arrives in the box. It flags "may contain traces of" apart from
    /// "contains" and lists the utensils a recipe needs.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: true,
        HasPantryItems: false,
        HasNutrition: true,
        HasTraceAllergens: true,
        HasUtensils: true,
        PortionSizes: [2, 3, 4]);

    /// <summary>
    /// One row per recipe per portion count. Prep and total time are recipe
    /// level here, unlike Gousto where they vary by yield. Nutrition rows are
    /// pivoted by their published names - the strings are pinned to what the
    /// committed fixtures carry, and the projection tests break if they drift.
    /// Traces arrive as separate allergen entries slugged "traces-of-x"; the
    /// trace aggregate strips the prefix to the canonical slug so one exclusion
    /// value matches both arrays.
    /// </summary>
    public string RecipeViewSql => """
        SELECT
            'hellofresh'          AS source,
            r.id                  AS recipe_id,
            r.external_id         AS external_key,
            r.name                AS name,
            r.headline            AS headline,
            r.description         AS description,
            y.portions            AS portions,
            r.prep_minutes        AS prep_minutes,
            r.total_minutes       AS total_minutes,
            r.difficulty          AS difficulty,
            n.kcal                AS kcal,
            n.energy_kj           AS energy_kj,
            n.fat_g               AS fat_g,
            n.saturates_g         AS saturates_g,
            n.carbs_g             AS carbs_g,
            n.sugars_g            AS sugars_g,
            n.fibre_g             AS fibre_g,
            n.protein_g           AS protein_g,
            n.salt_g              AS salt_g,
            r.serving_size_grams  AS serving_size_g,
            r.average_rating      AS rating_avg,
            r.ratings_count       AS rating_count,
            COALESCE(cu.slugs, ARRAY[]::text[]) AS cuisines,
            COALESCE(al.slugs, ARRAY[]::text[]) AS allergens,
            COALESCE(tr.slugs, ARRAY[]::text[]) AS trace_allergens,
            COALESCE(tg.names, ARRAY[]::text[]) AS tags,
            r.image_url           AS image_url,
            r.website_url         AS website_url,
            concat_ws(' ', r.name, r.headline, r.description, ing.names) AS search_text
        FROM hellofresh.recipe r
        JOIN hellofresh.recipe_yield y ON y.recipe_id = r.id
        LEFT JOIN LATERAL (
            SELECT
                max(rn.amount) FILTER (WHERE rn.name = 'Energy (kcal)')      AS kcal,
                max(rn.amount) FILTER (WHERE rn.name = 'Energy (kJ)')        AS energy_kj,
                max(rn.amount) FILTER (WHERE rn.name = 'Fat')                AS fat_g,
                max(rn.amount) FILTER (WHERE rn.name = 'of which saturates') AS saturates_g,
                max(rn.amount) FILTER (WHERE rn.name = 'Carbohydrate')       AS carbs_g,
                max(rn.amount) FILTER (WHERE rn.name = 'of which sugars')    AS sugars_g,
                max(rn.amount) FILTER (WHERE rn.name = 'Dietary Fibre')      AS fibre_g,
                max(rn.amount) FILTER (WHERE rn.name = 'Protein')            AS protein_g,
                max(rn.amount) FILTER (WHERE rn.name = 'Salt')               AS salt_g
            FROM hellofresh.recipe_nutrition rn
            WHERE rn.recipe_id = r.id
        ) n ON true
        LEFT JOIN LATERAL (
            SELECT string_agg(DISTINCT i.name, ' ') AS names
            FROM hellofresh.recipe_yield y2
            JOIN hellofresh.recipe_yield_ingredient yi ON yi.yield_id = y2.id
            JOIN hellofresh.ingredient i ON i.id = yi.ingredient_id
            WHERE y2.recipe_id = r.id
        ) ing ON true
        LEFT JOIN LATERAL (
            SELECT array_agg(c.slug::text ORDER BY c.slug) AS slugs
            FROM hellofresh.recipe_cuisine rc
            JOIN hellofresh.cuisine c ON c.id = rc.cuisine_id
            WHERE rc.recipe_id = r.id
        ) cu ON true
        LEFT JOIN LATERAL (
            SELECT array_agg(a.slug::text ORDER BY a.slug) AS slugs
            FROM hellofresh.recipe_allergen ra
            JOIN hellofresh.allergen a ON a.id = ra.allergen_id
            WHERE ra.recipe_id = r.id AND NOT ra.traces_of
        ) al ON true
        LEFT JOIN LATERAL (
            SELECT array_agg(DISTINCT regexp_replace(a.slug::text, '^traces-of-', '')) AS slugs
            FROM hellofresh.recipe_allergen ra
            JOIN hellofresh.allergen a ON a.id = ra.allergen_id
            WHERE ra.recipe_id = r.id AND ra.traces_of
        ) tr ON true
        LEFT JOIN LATERAL (
            SELECT array_agg(t.name::text ORDER BY t.name) AS names
            FROM hellofresh.recipe_tag rt
            JOIN hellofresh.tag t ON t.id = rt.tag_id
            WHERE rt.recipe_id = r.id
        ) tg ON true
        """;

    public string RecipeIngredientViewSql => """
        SELECT
            'hellofresh'  AS source,
            y.recipe_id   AS recipe_id,
            y.portions    AS portions,
            i.name        AS ingredient_name,
            yi.amount     AS amount,
            yi.unit       AS unit
        FROM hellofresh.recipe_yield_ingredient yi
        JOIN hellofresh.recipe_yield y ON y.id = yi.yield_id
        JOIN hellofresh.ingredient i ON i.id = yi.ingredient_id
        """;
}
