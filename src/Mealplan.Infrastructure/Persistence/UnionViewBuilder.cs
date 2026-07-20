using Mealplan.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Builds the cross-source read views by UNIONing whatever each registered
/// source contributes.
/// </summary>
/// <remarks>
/// <para>
/// Rebuilt at startup rather than written into a migration, because a migration
/// would have to be edited every time a source is added - the coupling the
/// per-source schemas exist to avoid. There are no shared normalised tables;
/// these views are read-only projections over each source's own tables.
/// </para>
/// <para>
/// v_recipe is materialized: computing its lateral aggregates live put a
/// representative search at seconds against ~75k rows, an order of magnitude
/// over the performance gate, so the designed fallback applies - refreshed
/// here at startup and by <see cref="RecipeViewRefresher"/> after each
/// normalise run. The stored to_tsvector column plus its GIN index is what
/// makes free text cheap; the trigram GiST index serves the typo fallback.
/// v_recipe_ingredient stays a plain view - it has no laterals and reads
/// straight off indexed tables. The vocabulary views (v_allergen, v_cuisine,
/// v_tag, v_ingredient) stay plain too: they aggregate over vocabulary tables
/// of at most a few thousand rows.
/// </para>
/// </remarks>
public class UnionViewBuilder(ILogger<UnionViewBuilder> logger)
{
    public async Task BuildAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        var schemas = scope.ServiceProvider.GetServices<ISourceSchema>()
            .OrderBy(s => s.Source, StringComparer.Ordinal)
            .ToList();

        var db = scope.ServiceProvider.GetRequiredService<ScrapeDbContext>();

        // Building the materialized view runs every source's laterals once
        // over the full data set; give it room beyond the default 30s.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        if (schemas.Count == 0)
        {
            // Empty views keep the MCP server answering "no recipes" rather than
            // failing on a missing relation.
            await db.Database.ExecuteSqlRawAsync(EmptyViews(), ct);
            logger.LogWarning("No sources registered; cross-source views are empty");
            return;
        }

        var sql = string.Join(
            "\n",
            RecipeView(string.Join("\nUNION ALL\n", schemas.Select(s => s.RecipeViewSql.Trim()))),
            CreateView("v_recipe_ingredient", schemas.Select(s => s.RecipeIngredientViewSql)),
            CreateView("v_allergen", schemas.Select(s => s.AllergenViewSql)),
            CreateView("v_cuisine", schemas.Select(s => s.CuisineViewSql)),
            CreateView("v_tag", schemas.Select(s => s.TagViewSql)),
            CreateView("v_ingredient", schemas.Select(s => s.IngredientViewSql)));

        await db.Database.ExecuteSqlRawAsync(sql, ct);

        logger.LogInformation(
            "Cross-source views rebuilt over {Sources}",
            string.Join(", ", schemas.Select(s => s.Source)));
    }

    private static string RecipeView(string union) =>
        $"""
        {DropRecipeView}
        CREATE MATERIALIZED VIEW public.v_recipe AS
        SELECT u.*, to_tsvector('english', u.search_text) AS search_tsv
        FROM (
        {union}
        ) u;
        {RecipeViewIndexes}
        ANALYZE public.v_recipe;
        """;

    /// <summary>
    /// The name may be held by the plain view of an older deployment or by the
    /// current materialized one, and each kind refuses the other's DROP.
    /// </summary>
    private const string DropRecipeView = """
        DO $$ BEGIN
            IF EXISTS (SELECT FROM pg_views
                       WHERE schemaname = 'public' AND viewname = 'v_recipe') THEN
                DROP VIEW public.v_recipe;
            END IF;
        END $$;
        DROP MATERIALIZED VIEW IF EXISTS public.v_recipe;
        """;

    /// <summary>
    /// The unique key doubles as the get_recipe lookup path; portions serves
    /// every search's base filter; the GIN and GiST pair back the two text
    /// predicates. Statistics refresh alongside because the planner chose
    /// catastrophic plans off stale ones.
    /// </summary>
    private const string RecipeViewIndexes = """
        CREATE UNIQUE INDEX ux_v_recipe_identity ON public.v_recipe (source, recipe_id, portions);
        CREATE INDEX ix_v_recipe_portions ON public.v_recipe (portions);
        CREATE INDEX ix_v_recipe_search_tsv ON public.v_recipe USING gin (search_tsv);
        CREATE INDEX ix_v_recipe_search_trgm ON public.v_recipe
            USING gist (search_text gist_trgm_ops(siglen=256));
        """;

    private static string CreateView(string name, IEnumerable<string> parts) =>
        $"""
        CREATE OR REPLACE VIEW public.{name} AS
        {string.Join("\nUNION ALL\n", parts.Select(p => p.Trim()))};
        """;

    private static string EmptyViews()
    {
        var recipe = Typed(RecipeViewColumns.Recipe);

        var plain = new (string Name, IReadOnlyList<string> Columns)[]
        {
            ("v_recipe_ingredient", RecipeViewColumns.RecipeIngredient),
            ("v_allergen", RecipeViewColumns.Allergen),
            ("v_cuisine", RecipeViewColumns.Cuisine),
            ("v_tag", RecipeViewColumns.Tag),
            ("v_ingredient", RecipeViewColumns.Ingredient),
        };

        return $"""
            {DropRecipeView}
            CREATE MATERIALIZED VIEW public.v_recipe AS
            SELECT {recipe}, NULL::tsvector AS search_tsv WHERE false;
            {RecipeViewIndexes}
            {string.Join(
                "\n",
                plain.Select(view =>
                    $"CREATE OR REPLACE VIEW public.{view.Name} AS SELECT {Typed(view.Columns)} WHERE false;"))}
            """;
    }

    /// <summary>
    /// Postgres needs a type for every column of a view that selects no rows.
    /// The MCP layer only reads these as text or numbers, so text and the
    /// numeric columns below are enough to keep the shape valid.
    /// </summary>
    private static string Typed(IReadOnlyList<string> columns) =>
        string.Join(", ", columns.Select(column => column switch
        {
            "portions" or "prep_minutes" or "total_minutes" or "difficulty"
                or "rating_count" or "recipe_count" or "trace_count" =>
                $"NULL::integer AS {column}",
            "kcal" or "amount" or "energy_kj" or "fat_g" or "saturates_g"
                or "carbs_g" or "sugars_g" or "fibre_g" or "protein_g"
                or "salt_g" or "serving_size_g" or "rating_avg" =>
                $"NULL::double precision AS {column}",
            "cuisines" or "allergens" or "trace_allergens" or "tags" =>
                $"NULL::text[] AS {column}",
            "recipe_id" => $"NULL::uuid AS {column}",
            _ => $"NULL::text AS {column}",
        }));
}
