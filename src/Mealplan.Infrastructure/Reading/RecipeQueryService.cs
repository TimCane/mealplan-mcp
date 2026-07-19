using System.Data;
using System.Text;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mealplan.Infrastructure.Reading;

/// <summary>
/// Reads recipes through the cross-source views. Hand-written SQL rather than
/// LINQ: the views are keyless projections and the filters are dynamic, so the
/// query is clearer written out than composed.
/// </summary>
public class RecipeQueryService
{
    private readonly ScrapeDbContext db;
    private readonly IReadOnlyDictionary<string, ISourceSchema> schemas;

    /// <summary>
    /// Takes the schemas rather than the whole SourceRegistry: reading needs to
    /// know where a source's tables live and what it can report, not how to
    /// crawl it. The MCP host has no business requiring a crawler to exist.
    /// </summary>
    public RecipeQueryService(ScrapeDbContext db, IEnumerable<ISourceSchema> schemas)
    {
        this.db = db;
        this.schemas = schemas.ToDictionary(s => s.Source, StringComparer.OrdinalIgnoreCase);
    }

    private ISourceSchema Schema(string source) =>
        schemas.TryGetValue(source, out var schema)
            ? schema
            : throw new KeyNotFoundException($"No schema registered for source '{source}'.");

    public async Task<SearchResult> SearchAsync(
        RecipeSearchQuery query,
        CancellationToken ct = default)
    {
        var where = new StringBuilder("r.portions = @portions");
        var parameters = new List<NpgsqlParameter>
        {
            new("portions", query.Portions),
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            // Full text first, then trigram word similarity. Plain % compares
            // whole strings, so one misspelled word against a long recipe title
            // never reaches the threshold; <% scores the query against the best
            // matching run of words instead.
            where.Append("""
                 AND (
                    to_tsvector('english', r.search_text)
                        @@ websearch_to_tsquery('english', @q)
                    OR @q <% r.search_text
                 )
                """);

            parameters.Add(new("q", query.Query));
        }

        AddArrayFilter(where, parameters, "sources", query.Sources, "r.source = ANY(@sources)");
        AddArrayFilter(where, parameters, "cuisines", query.Cuisines, "r.cuisines && @cuisines");

        AddArrayFilter(
            where,
            parameters,
            "exclude_allergens",
            query.ExcludeAllergens,
            "NOT (r.allergens && @exclude_allergens)");

        if (query.MaxPrepMinutes is { } maxPrep)
        {
            // A recipe with no published prep time is excluded rather than
            // assumed quick - guessing here would put it on a weeknight plan.
            where.Append(" AND r.prep_minutes IS NOT NULL AND r.prep_minutes <= @max_prep");
            parameters.Add(new("max_prep", maxPrep));
        }

        if (query.MaxKcal is { } maxKcal)
        {
            where.Append(" AND r.kcal IS NOT NULL AND r.kcal <= @max_kcal");
            parameters.Add(new("max_kcal", maxKcal));
        }

        if (query.IncludeIngredients is { Count: > 0 } ingredients)
        {
            // Every named ingredient must appear, so the count of distinct
            // matches has to equal the number asked for.
            where.Append("""
                 AND (
                    SELECT count(DISTINCT needle)
                    FROM unnest(@include_ingredients) AS needle
                    WHERE EXISTS (
                        SELECT 1 FROM public.v_recipe_ingredient i
                        WHERE i.recipe_id = r.recipe_id
                          AND i.source = r.source
                          AND i.portions = r.portions
                          AND i.ingredient_name ILIKE '%' || needle || '%'
                    )
                 ) = cardinality(@include_ingredients)
                """);

            parameters.Add(new("include_ingredients", ingredients.ToArray()));
        }

        var total = await ScalarAsync(
            $"SELECT count(*) FROM public.v_recipe r WHERE {where}",
            parameters,
            ct);

        var take = Math.Clamp(query.Take, 1, 100);

        var rows = await ReadAsync(
            $"""
             SELECT r.source, r.recipe_id, r.external_key, r.name, r.headline,
                    r.portions, r.prep_minutes, r.total_minutes, r.difficulty,
                    r.kcal, r.cuisines, r.allergens, r.tags, r.image_url
             FROM public.v_recipe r
             WHERE {where}
             ORDER BY r.name, r.source
             OFFSET @skip LIMIT @take
             """,
            [.. parameters, new NpgsqlParameter("skip", Math.Max(query.Skip, 0)),
                new NpgsqlParameter("take", take)],
            reader => new RecipeSummary(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                Nullable(reader, 4, r => r.GetString(4)),
                reader.GetInt32(5),
                Nullable(reader, 6, r => r.GetInt32(6)),
                Nullable(reader, 7, r => r.GetInt32(7)),
                Nullable(reader, 8, r => r.GetInt32(8)),
                Nullable(reader, 9, r => r.GetDouble(9)),
                reader.GetFieldValue<string[]>(10),
                reader.GetFieldValue<string[]>(11),
                reader.GetFieldValue<string[]>(12),
                Nullable(reader, 13, r => r.GetString(13))),
            ct);

        return new SearchResult(rows, (int)total, query.Skip, take);
    }

    public async Task<RecipeDetail?> GetAsync(
        string source,
        Guid recipeId,
        int portions,
        CancellationToken ct = default)
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("source", source),
            new("recipe_id", recipeId),
            new("portions", portions),
        };

        var summaries = await ReadAsync(
            """
            SELECT r.source, r.recipe_id, r.external_key, r.name, r.headline,
                   r.description, r.portions, r.prep_minutes, r.total_minutes,
                   r.difficulty, r.kcal, r.cuisines, r.allergens, r.tags, r.image_url
            FROM public.v_recipe r
            WHERE r.source = @source AND r.recipe_id = @recipe_id AND r.portions = @portions
            """,
            parameters,
            reader => new
            {
                Source = reader.GetString(0),
                RecipeId = reader.GetGuid(1),
                ExternalKey = reader.GetString(2),
                Name = reader.GetString(3),
                Headline = Nullable(reader, 4, r => r.GetString(4)),
                Description = Nullable(reader, 5, r => r.GetString(5)),
                Portions = reader.GetInt32(6),
                PrepMinutes = Nullable(reader, 7, r => r.GetInt32(7)),
                TotalMinutes = Nullable(reader, 8, r => r.GetInt32(8)),
                Difficulty = Nullable(reader, 9, r => r.GetInt32(9)),
                Kcal = Nullable(reader, 10, r => r.GetDouble(10)),
                Cuisines = reader.GetFieldValue<string[]>(11),
                Allergens = reader.GetFieldValue<string[]>(12),
                Tags = reader.GetFieldValue<string[]>(13),
                ImageUrl = Nullable(reader, 14, r => r.GetString(14)),
            },
            ct);

        var summary = summaries.FirstOrDefault();
        if (summary is null)
        {
            return null;
        }

        var ingredients = await ReadAsync(
            """
            SELECT i.ingredient_name, i.amount, i.unit
            FROM public.v_recipe_ingredient i
            WHERE i.source = @source AND i.recipe_id = @recipe_id AND i.portions = @portions
            ORDER BY i.ingredient_name
            """,
            parameters,
            reader => new RecipeIngredient(
                reader.GetString(0),
                Nullable(reader, 1, r => r.GetDouble(1)),
                Nullable(reader, 2, r => r.GetString(2))),
            ct);

        var schema = Schema(source);
        var capabilities = schema.Capabilities;

        return new RecipeDetail(
            summary.Source,
            summary.RecipeId,
            summary.ExternalKey,
            summary.Name,
            summary.Headline,
            summary.Description,
            summary.Portions,
            summary.PrepMinutes,
            summary.TotalMinutes,
            summary.Difficulty,
            summary.Kcal,
            summary.Cuisines,
            summary.Allergens,
            summary.Tags,
            summary.ImageUrl,
            ingredients,
            await StepsAsync(source, summary.RecipeId, ct),
            await PantryItemsAsync(source, summary.RecipeId, ct),
            new SourceNotes(
                capabilities.HasIngredientQuantities,
                capabilities.HasPantryItems,
                capabilities.HasIngredientQuantities
                    ? null
                    : $"{schema.DisplayName} does not publish ingredient quantities, "
                        + "so amounts are absent rather than zero."));
    }

    /// <summary>
    /// Steps and pantry items live only in a source's own tables and have no
    /// common shape worth a view, so they are read per source.
    /// </summary>
    private async Task<IReadOnlyList<string>> StepsAsync(
        string source,
        Guid recipeId,
        CancellationToken ct)
    {
        var schema = Schema(source).SchemaName;

        var column = source == "gousto" ? "instruction_text" : "instructions";
        var order = source == "gousto" ? "\"order\"" : "index";

        return await ReadAsync(
            $"""
             SELECT {column} FROM {Quote(schema)}.recipe_step
             WHERE recipe_id = @recipe_id ORDER BY {order}
             """,
            [new NpgsqlParameter("recipe_id", recipeId)],
            reader => reader.GetString(0),
            ct);
    }

    private async Task<IReadOnlyList<string>> PantryItemsAsync(
        string source,
        Guid recipeId,
        CancellationToken ct)
    {
        if (!Schema(source).Capabilities.HasPantryItems)
        {
            return [];
        }

        var schema = Schema(source).SchemaName;

        return await ReadAsync(
            $"""
             SELECT title FROM {Quote(schema)}.pantry_item
             WHERE recipe_id = @recipe_id ORDER BY title
             """,
            [new NpgsqlParameter("recipe_id", recipeId)],
            reader => reader.GetString(0),
            ct);
    }

    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(CancellationToken ct = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var rows = await ReadAsync(
            "SELECT source, count(DISTINCT recipe_id) FROM public.v_recipe GROUP BY source",
            [],
            reader => (Source: reader.GetString(0), Count: (int)reader.GetInt64(1)),
            ct);

        foreach (var row in rows)
        {
            counts[row.Source] = row.Count;
        }

        return schemas.Values
            .OrderBy(s => s.Source, StringComparer.Ordinal)
            .Select(s => new SourceInfo(
                s.Source,
                s.DisplayName,
                counts.GetValueOrDefault(s.Source),
                s.Capabilities.HasIngredientQuantities,
                s.Capabilities.HasPantryItems,
                s.Capabilities.HasNutrition,
                s.Capabilities.PortionSizes))
            .ToList();
    }

    public async Task<IReadOnlyList<ScrapeStatus>> ScrapeStatusAsync(CancellationToken ct = default)
    {
        var statuses = new List<ScrapeStatus>();

        foreach (var schema in schemas.Values.OrderBy(s => s.Source, StringComparer.Ordinal))
        {
            var run = await db.Runs
                .Where(r => r.Source == schema.Source)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync(ct);

            var stored = await db.Documents.CountAsync(d => d.Source == schema.Source, ct);
            var pending = await db.Documents
                .CountAsync(d => d.Source == schema.Source && d.NormalizedAt == null, ct);

            statuses.Add(new ScrapeStatus(
                schema.Source,
                run?.Status.ToString(),
                run?.StartedAt,
                run?.FinishedAt,
                stored,
                pending,
                run?.Error));
        }

        return statuses;
    }

    private static void AddArrayFilter(
        StringBuilder where,
        List<NpgsqlParameter> parameters,
        string name,
        IReadOnlyList<string>? values,
        string clause)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        where.Append(" AND ").Append(clause);
        parameters.Add(new NpgsqlParameter(name, values.ToArray()));
    }

    private static T? Nullable<T>(NpgsqlDataReader reader, int ordinal, Func<NpgsqlDataReader, T> read)
        where T : struct =>
        reader.IsDBNull(ordinal) ? null : read(reader);

    private static string? Nullable(
        NpgsqlDataReader reader,
        int ordinal,
        Func<NpgsqlDataReader, string> read) =>
        reader.IsDBNull(ordinal) ? null : read(reader);

    /// <summary>
    /// Schema names come from registered sources, never from a caller, but they
    /// are still quoted rather than interpolated raw.
    /// </summary>
    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private async Task<long> ScalarAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var command = await CommandAsync(sql, parameters, ct);
        var result = await command.ExecuteScalarAsync(ct);

        return result is long value ? value : 0;
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct)
    {
        await using var command = await CommandAsync(sql, parameters, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private async Task<NpgsqlCommand> CommandAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var command = new NpgsqlCommand(sql, connection);

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(Clone(parameter));
        }

        return command;
    }

    /// <summary>A parameter cannot belong to two commands, and the count query
    /// and the page query share a list.</summary>
    private static NpgsqlParameter Clone(NpgsqlParameter parameter) =>
        new(parameter.ParameterName, parameter.Value);
}
