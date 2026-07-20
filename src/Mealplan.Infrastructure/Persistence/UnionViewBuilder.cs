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
/// Rebuilt at startup rather than written into a migration, because a migration
/// would have to be edited every time a source is added - the coupling the
/// per-source schemas exist to avoid. There are no shared normalised tables;
/// these views are read-only projections over each source's own tables.
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
            CreateView("v_recipe", schemas.Select(s => s.RecipeViewSql)),
            CreateView("v_recipe_ingredient", schemas.Select(s => s.RecipeIngredientViewSql)));

        await db.Database.ExecuteSqlRawAsync(sql, ct);

        logger.LogInformation(
            "Cross-source views rebuilt over {Sources}",
            string.Join(", ", schemas.Select(s => s.Source)));
    }

    private static string CreateView(string name, IEnumerable<string> parts) =>
        $"""
        CREATE OR REPLACE VIEW public.{name} AS
        {string.Join("\nUNION ALL\n", parts.Select(p => p.Trim()))};
        """;

    private static string EmptyViews()
    {
        var recipe = Typed(RecipeViewColumns.Recipe);
        var ingredient = Typed(RecipeViewColumns.RecipeIngredient);

        return $"""
            CREATE OR REPLACE VIEW public.v_recipe AS SELECT {recipe} WHERE false;
            CREATE OR REPLACE VIEW public.v_recipe_ingredient AS SELECT {ingredient} WHERE false;
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
                or "rating_count" =>
                $"NULL::integer AS {column}",
            "kcal" or "amount" or "energy_kj" or "fat_g" or "saturates_g"
                or "carbs_g" or "sugars_g" or "fibre_g" or "protein_g"
                or "salt_g" or "serving_size_g" or "rating_avg" =>
                $"NULL::double precision AS {column}",
            "cuisines" or "allergens" or "tags" => $"NULL::text[] AS {column}",
            "recipe_id" => $"NULL::uuid AS {column}",
            _ => $"NULL::text AS {column}",
        }));
}
