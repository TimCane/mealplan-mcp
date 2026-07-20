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

    /// <summary>
    /// Columns a nutrient filter may touch. The enum never reaches the SQL
    /// string: the column comes from this map and the bounds travel as
    /// parameters.
    /// </summary>
    internal static readonly IReadOnlyDictionary<Nutrient, string> NutrientColumns =
        new Dictionary<Nutrient, string>
        {
            [Nutrient.Kcal] = "kcal",
            [Nutrient.Kj] = "energy_kj",
            [Nutrient.Fat] = "fat_g",
            [Nutrient.Saturates] = "saturates_g",
            [Nutrient.Carbs] = "carbs_g",
            [Nutrient.Sugars] = "sugars_g",
            [Nutrient.Fibre] = "fibre_g",
            [Nutrient.Protein] = "protein_g",
            [Nutrient.Salt] = "salt_g",
        };

    public async Task<Page<RecipeSummary>> SearchAsync(
        RecipeSearchQuery query,
        CancellationToken ct = default)
    {
        var where = new StringBuilder("r.portions = @portions");
        var parameters = new List<NpgsqlParameter>
        {
            new("portions", query.Portions),
        };

        string? trigramClause = null;
        var textClause = string.Empty;

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            // Full text against the stored tsvector - its GIN index answers in
            // milliseconds. The trigram arm is not OR-ed in: <% cannot serve
            // that form from an index, and evaluating it per row put every
            // text search over the performance gate. It runs as a second pass
            // instead, only when full text finds nothing.
            textClause = " AND r.search_tsv @@ websearch_to_tsquery('english', @q)";

            // Plain % compares whole strings, so one misspelled word against a
            // long recipe title never reaches the threshold; <% scores the
            // query against the best matching run of words instead.
            trigramClause = " AND @q <% r.search_text";

            parameters.Add(new("q", query.Query));
        }

        AddArrayFilter(where, parameters, "sources", query.Sources, "r.source = ANY(@sources)");
        AddArrayFilter(where, parameters, "cuisines", query.Cuisines, "r.cuisines && @cuisines");

        // Traces count as carrying the allergen unless the caller relaxes it -
        // over-excluding is the only safe default here.
        AddArrayFilter(
            where,
            parameters,
            "exclude_allergens",
            query.ExcludeAllergens,
            query.ExcludeTraces
                ? "NOT ((r.allergens || r.trace_allergens) && @exclude_allergens)"
                : "NOT (r.allergens && @exclude_allergens)");

        if (query.MaxPrepMinutes is { } maxPrep)
        {
            // A recipe with no published prep time is excluded rather than
            // assumed quick - guessing here would put it on a weeknight plan.
            where.Append(" AND r.prep_minutes IS NOT NULL AND r.prep_minutes <= @max_prep");
            parameters.Add(new("max_prep", maxPrep));
        }

        AppendNutrientFilters(where, parameters, query.NutrientFilters);

        if (query.MinRating is { } minRating)
        {
            // An unrated recipe is excluded rather than assumed fine - the
            // same null-excludes rule as every other filter.
            where.Append(" AND r.rating_avg IS NOT NULL AND r.rating_avg >= @min_rating");
            parameters.Add(new("min_rating", minRating));
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
            $"SELECT count(*) FROM public.v_recipe r WHERE {where}{textClause}",
            parameters,
            ct);

        if (total == 0 && trigramClause is not null)
        {
            // The typo fallback: same filters, trigram text match.
            textClause = trigramClause;

            total = await ScalarAsync(
                $"SELECT count(*) FROM public.v_recipe r WHERE {where}{textClause}",
                parameters,
                ct);
        }

        var take = Math.Clamp(query.Take, 1, 100);

        if (query.Sort == RecipeSort.Random && query.Seed is { } seed)
        {
            // setseed pins the sequence random() draws from, so the shuffle is
            // stable within a seed - paging does not tear - and a new seed
            // reshuffles. Unseeded random stays a fresh draw per call.
            await ExecuteAsync(
                "SELECT setseed(@seed)",
                [new NpgsqlParameter("seed", NormalizeSeed(seed))],
                ct);
        }

        var rows = await ReadAsync(
            $"""
             SELECT r.source, r.recipe_id, r.external_key, r.name, r.headline,
                    r.portions, r.prep_minutes, r.total_minutes, r.difficulty,
                    r.kcal, r.energy_kj, r.fat_g, r.saturates_g, r.carbs_g,
                    r.sugars_g, r.fibre_g, r.protein_g, r.salt_g,
                    r.rating_avg, r.rating_count,
                    r.cuisines, r.allergens, r.trace_allergens, r.tags, r.image_url
             FROM public.v_recipe r
             WHERE {where}{textClause}
             ORDER BY {OrderBy(query.Sort)}
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
                ReadNutrition(reader, 9),
                Nullable(reader, 18, r => r.GetDouble(18)),
                Nullable(reader, 19, r => r.GetInt32(19)),
                reader.GetFieldValue<string[]>(20),
                reader.GetFieldValue<string[]>(21),
                reader.GetFieldValue<string[]>(22),
                reader.GetFieldValue<string[]>(23),
                Nullable(reader, 24, r => r.GetString(24))),
            ct);

        return new Page<RecipeSummary>(rows, (int)total, query.Skip, take);
    }

    /// <summary>
    /// A filter on a nutrient excludes recipes with no published value for it,
    /// even when only one bound is set - the null-excludes rule.
    /// </summary>
    internal static void AppendNutrientFilters(
        StringBuilder where,
        List<NpgsqlParameter> parameters,
        IReadOnlyList<NutrientFilter>? filters)
    {
        if (filters is not { Count: > 0 })
        {
            return;
        }

        foreach (var (filter, index) in filters.Select((f, i) => (f, i)))
        {
            var column = NutrientColumns[filter.Nutrient];

            where.Append($" AND r.{column} IS NOT NULL");

            if (filter.Min is { } min)
            {
                where.Append($" AND r.{column} >= @nutrient_min_{index}");
                parameters.Add(new($"nutrient_min_{index}", min));
            }

            if (filter.Max is { } max)
            {
                where.Append($" AND r.{column} <= @nutrient_max_{index}");
                parameters.Add(new($"nutrient_max_{index}", max));
            }
        }
    }

    internal static string OrderBy(RecipeSort sort) => sort switch
    {
        RecipeSort.Rating =>
            "r.rating_avg DESC NULLS LAST, r.rating_count DESC NULLS LAST, r.name, r.source",
        RecipeSort.Kcal => "r.kcal ASC NULLS LAST, r.name, r.source",
        RecipeSort.Random => "random()",
        _ => "r.name, r.source",
    };

    /// <summary>setseed takes a double in [-1, 1); any integer seed folds in.</summary>
    internal static double NormalizeSeed(int seed) =>
        (seed % 1_000_000) / 1_000_000d;

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
                   r.difficulty, r.kcal, r.energy_kj, r.fat_g, r.saturates_g,
                   r.carbs_g, r.sugars_g, r.fibre_g, r.protein_g, r.salt_g,
                   r.serving_size_g, r.rating_avg, r.rating_count,
                   r.cuisines, r.allergens, r.trace_allergens, r.tags,
                   r.image_url, r.website_url
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
                Nutrition = ReadNutrition(reader, 10),
                ServingSizeGrams = Nullable(reader, 19, r => r.GetDouble(19)),
                RatingAverage = Nullable(reader, 20, r => r.GetDouble(20)),
                RatingCount = Nullable(reader, 21, r => r.GetInt32(21)),
                Cuisines = reader.GetFieldValue<string[]>(22),
                Allergens = reader.GetFieldValue<string[]>(23),
                TraceAllergens = reader.GetFieldValue<string[]>(24),
                Tags = reader.GetFieldValue<string[]>(25),
                ImageUrl = Nullable(reader, 26, r => r.GetString(26)),
                WebsiteUrl = Nullable(reader, 27, r => r.GetString(27)),
            },
            ct);

        var offered = await OfferedPortionsAsync(source, recipeId, ct);

        var summary = summaries.FirstOrDefault();
        if (summary is null)
        {
            if (offered.Count == 0)
            {
                // Nothing at any portion count: the id is unknown, not-found.
                return null;
            }

            // The id is real; only the portion count is wrong. Failing with the
            // counts that work lets the caller learn the fix from the failure.
            throw new PortionsNotOfferedException(source, recipeId, portions, offered);
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

        return new RecipeDetail(
            summary.Source,
            summary.RecipeId,
            summary.ExternalKey,
            summary.Name,
            summary.Headline,
            summary.Description,
            summary.Portions,
            offered,
            summary.PrepMinutes,
            summary.TotalMinutes,
            summary.Difficulty,
            summary.Nutrition,
            summary.ServingSizeGrams,
            summary.RatingAverage,
            summary.RatingCount,
            summary.Cuisines,
            summary.Allergens,
            summary.TraceAllergens,
            summary.Tags,
            summary.ImageUrl,
            summary.WebsiteUrl,
            await UpdatedAtAsync(source, summary.RecipeId, ct),
            ingredients,
            await StepsAsync(source, summary.RecipeId, ct),
            await PantryItemsAsync(source, summary.RecipeId, ct),
            await UtensilsAsync(source, summary.RecipeId, ct),
            Notes(schema));
    }

    private async Task<IReadOnlyList<int>> OfferedPortionsAsync(
        string source,
        Guid recipeId,
        CancellationToken ct)
    {
        return await ReadAsync(
            """
            SELECT DISTINCT r.portions FROM public.v_recipe r
            WHERE r.source = @source AND r.recipe_id = @recipe_id
            ORDER BY r.portions
            """,
            [new NpgsqlParameter("source", source), new NpgsqlParameter("recipe_id", recipeId)],
            reader => reader.GetInt32(0),
            ct);
    }

    /// <summary>
    /// When the recipe last changed upstream. Normalisation only reruns when a
    /// crawl stores a payload with a new content hash, so the stamp tracks
    /// observed upstream change, not crawl frequency.
    /// </summary>
    private async Task<DateTimeOffset?> UpdatedAtAsync(
        string source,
        Guid recipeId,
        CancellationToken ct)
    {
        var schema = Schema(source).SchemaName;

        var rows = await ReadAsync(
            $"SELECT updated_at FROM {Quote(schema)}.recipe WHERE id = @recipe_id",
            [new NpgsqlParameter("recipe_id", recipeId)],
            reader => reader.GetFieldValue<DateTimeOffset>(0),
            ct);

        return rows.Count == 0 ? null : rows[0];
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

    private async Task<IReadOnlyList<string>> UtensilsAsync(
        string source,
        Guid recipeId,
        CancellationToken ct)
    {
        if (!Schema(source).Capabilities.HasUtensils)
        {
            return [];
        }

        var schema = Schema(source).SchemaName;

        return await ReadAsync(
            $"""
             SELECT u.name FROM {Quote(schema)}.recipe_utensil ru
             JOIN {Quote(schema)}.utensil u ON u.id = ru.utensil_id
             WHERE ru.recipe_id = @recipe_id ORDER BY u.name
             """,
            [new NpgsqlParameter("recipe_id", recipeId)],
            reader => reader.GetString(0),
            ct);
    }

    /// <summary>
    /// The caveat spells out each gap in prose because the flags alone are easy
    /// to skim past when the stakes are allergens.
    /// </summary>
    private static SourceNotes Notes(ISourceSchema schema)
    {
        var capabilities = schema.Capabilities;
        var caveats = new List<string>();

        if (!capabilities.HasIngredientQuantities)
        {
            caveats.Add(
                $"{schema.DisplayName} does not publish ingredient quantities, "
                + "so amounts are absent rather than zero.");
        }

        if (!capabilities.HasTraceAllergens)
        {
            caveats.Add(
                $"{schema.DisplayName} does not publish may-contain-traces data, "
                + "so an empty traceAllergens means unknown, not none.");
        }

        return new SourceNotes(
            capabilities.HasIngredientQuantities,
            capabilities.HasPantryItems,
            capabilities.HasTraceAllergens,
            capabilities.HasUtensils,
            caveats.Count == 0 ? null : string.Join(" ", caveats));
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
                s.Capabilities.HasTraceAllergens,
                s.Capabilities.HasUtensils,
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

    /// <summary>Reads the nine panel columns laid out consecutively from
    /// <paramref name="first"/>, in <see cref="NutritionPanel"/> field order.</summary>
    private static NutritionPanel ReadNutrition(NpgsqlDataReader reader, int first)
    {
        double? At(int offset) =>
            reader.IsDBNull(first + offset) ? null : reader.GetDouble(first + offset);

        return new NutritionPanel(At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), At(8));
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

    private async Task ExecuteAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var command = await CommandAsync(sql, parameters, ct);
        await command.ExecuteNonQueryAsync(ct);
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
