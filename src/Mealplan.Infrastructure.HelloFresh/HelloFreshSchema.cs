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
    /// arrives in the box.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: true,
        HasPantryItems: false,
        HasNutrition: true,
        PortionSizes: [2, 3, 4]);

    /// <summary>
    /// One row per recipe per portion count. Prep and total time are recipe
    /// level here, unlike Gousto where they vary by yield.
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
            n.amount              AS kcal,
            COALESCE(cu.slugs, ARRAY[]::text[]) AS cuisines,
            COALESCE(al.slugs, ARRAY[]::text[]) AS allergens,
            COALESCE(tg.names, ARRAY[]::text[]) AS tags,
            r.image_url           AS image_url,
            concat_ws(' ', r.name, r.headline, r.description) AS search_text
        FROM hellofresh.recipe r
        JOIN hellofresh.recipe_yield y ON y.recipe_id = r.id
        LEFT JOIN hellofresh.recipe_nutrition n
            ON n.recipe_id = r.id AND n.name = 'Energy (kcal)'
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
            WHERE ra.recipe_id = r.id
        ) al ON true
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
