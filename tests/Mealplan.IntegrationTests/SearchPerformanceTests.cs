using System.Diagnostics;
using FluentAssertions;
using Mealplan.Infrastructure.Reading;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.IntegrationTests;

/// <summary>
/// The captured fixtures multiplied to the row count the plan's performance
/// gate is stated against. Deterministic md5-derived uuids keep every copied
/// child pointing at its copied parent without round trips. Own fixture, not
/// the shared collection: it mutates the data it loads.
/// </summary>
public sealed class MultipliedViewFixture : IAsyncLifetime
{
    public const int TargetViewRows = 75_000;

    private readonly CrossSourceViewFixture _inner = new();

    public int ViewRows { get; private set; }

    public RecipeQueryService CreateQueryService() => _inner.CreateQueryService();

    public async Task InitializeAsync()
    {
        await _inner.InitializeAsync();

        await using var db = _inner.ScrapeContext();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var baseRows = await CountAsync(db);
        var copies = TargetViewRows / baseRows;

        await db.Database.ExecuteSqlRawAsync(MultiplySql(copies));

        // The view materialized before the copies existed; without fresh
        // statistics the planner picked catastrophic plans against them.
        await db.Database.ExecuteSqlRawAsync(
            "ANALYZE; REFRESH MATERIALIZED VIEW public.v_recipe; ANALYZE public.v_recipe;");

        ViewRows = await CountAsync(db);
    }

    public async Task DisposeAsync() => await _inner.DisposeAsync();

    private static async Task<int> CountAsync(DbContext db) =>
        (int)await db.Database
            .SqlQueryRaw<long>("""SELECT count(*) AS "Value" FROM public.v_recipe""")
            .SingleAsync();

    /// <summary>
    /// Copies every recipe and the children the view reads (yields, yield
    /// ingredients, nutrition, taxonomies). Steps and pantry items are detail
    /// reads, not search reads, so they stay unmultiplied.
    /// </summary>
    private static string MultiplySql(int copies) => $"""
        INSERT INTO gousto.recipe
            (id, slug, gousto_uid, gousto_id, title, description, rating_average,
             rating_count, image_url, website_url, cuisine_id, updated_at)
        SELECT md5(r.id::text || g)::uuid, r.slug || '-' || g, r.gousto_uid,
               r.gousto_id, r.title || ' ' || g, r.description, r.rating_average,
               r.rating_count, r.image_url, r.website_url, r.cuisine_id, r.updated_at
        FROM gousto.recipe r, generate_series(1, {copies}) g;

        INSERT INTO gousto.recipe_yield (id, recipe_id, portions, prep_minutes, is_offered)
        SELECT md5(y.id::text || g)::uuid, md5(y.recipe_id::text || g)::uuid,
               y.portions, y.prep_minutes, y.is_offered
        FROM gousto.recipe_yield y, generate_series(1, {copies}) g;

        INSERT INTO gousto.recipe_yield_ingredient (yield_id, ingredient_id, sku_code, in_box)
        SELECT md5(yi.yield_id::text || g)::uuid, yi.ingredient_id, yi.sku_code, yi.in_box
        FROM gousto.recipe_yield_ingredient yi, generate_series(1, {copies}) g;

        INSERT INTO gousto.recipe_nutrition
            (id, recipe_id, basis, energy_kcal, energy_kj, fat_grams,
             saturated_fat_grams, carbs_grams, sugars_grams, fibre_grams,
             protein_grams, salt_grams, net_weight_grams)
        SELECT md5(n.id::text || g)::uuid, md5(n.recipe_id::text || g)::uuid,
               n.basis, n.energy_kcal, n.energy_kj, n.fat_grams,
               n.saturated_fat_grams, n.carbs_grams, n.sugars_grams, n.fibre_grams,
               n.protein_grams, n.salt_grams, n.net_weight_grams
        FROM gousto.recipe_nutrition n, generate_series(1, {copies}) g;

        INSERT INTO gousto.recipe_allergen (recipe_id, allergen_id)
        SELECT md5(ra.recipe_id::text || g)::uuid, ra.allergen_id
        FROM gousto.recipe_allergen ra, generate_series(1, {copies}) g;

        INSERT INTO gousto.recipe_category (recipe_id, category_id)
        SELECT md5(rc.recipe_id::text || g)::uuid, rc.category_id
        FROM gousto.recipe_category rc, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe
            (id, external_id, slug, name, headline, description, difficulty,
             prep_minutes, total_minutes, serving_size_grams, average_rating,
             ratings_count, image_url, website_url, category_id, updated_at)
        SELECT md5(r.id::text || g)::uuid, r.external_id || '-' || g,
               r.slug || '-' || g, r.name || ' ' || g, r.headline, r.description,
               r.difficulty, r.prep_minutes, r.total_minutes, r.serving_size_grams,
               r.average_rating, r.ratings_count, r.image_url, r.website_url,
               r.category_id, r.updated_at
        FROM hellofresh.recipe r, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe_yield (id, recipe_id, portions)
        SELECT md5(y.id::text || g)::uuid, md5(y.recipe_id::text || g)::uuid, y.portions
        FROM hellofresh.recipe_yield y, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe_yield_ingredient (yield_id, ingredient_id, amount, unit)
        SELECT md5(yi.yield_id::text || g)::uuid, yi.ingredient_id, yi.amount, yi.unit
        FROM hellofresh.recipe_yield_ingredient yi, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe_nutrition (id, recipe_id, name, amount, unit)
        SELECT md5(n.id::text || g)::uuid, md5(n.recipe_id::text || g)::uuid,
               n.name, n.amount, n.unit
        FROM hellofresh.recipe_nutrition n, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe_cuisine (recipe_id, cuisine_id)
        SELECT md5(rc.recipe_id::text || g)::uuid, rc.cuisine_id
        FROM hellofresh.recipe_cuisine rc, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe_allergen (recipe_id, allergen_id, traces_of)
        SELECT md5(ra.recipe_id::text || g)::uuid, ra.allergen_id, ra.traces_of
        FROM hellofresh.recipe_allergen ra, generate_series(1, {copies}) g;

        INSERT INTO hellofresh.recipe_tag (recipe_id, tag_id)
        SELECT md5(rt.recipe_id::text || g)::uuid, rt.tag_id
        FROM hellofresh.recipe_tag rt, generate_series(1, {copies}) g;
        """;
}

/// <summary>
/// The plan's performance gate: under ~300ms for a representative search at a
/// realistic row count the plain view ships; over it, v_recipe becomes a
/// materialized view. Doubles as the regression test for that decision.
/// </summary>
public sealed class SearchPerformanceTests(MultipliedViewFixture fixture)
    : IClassFixture<MultipliedViewFixture>
{
    private const int GateMilliseconds = 300;

    [Fact]
    public async Task A_representative_search_stays_under_the_gate()
    {
        fixture.ViewRows.Should().BeGreaterThanOrEqualTo(
            MultipliedViewFixture.TargetViewRows,
            "the gate is meaningless against a toy row count");

        var service = fixture.CreateQueryService();

        var query = new RecipeSearchQuery
        {
            Query = "steak",
            ExcludeAllergens = ["celery"],
            NutrientFilters = [new NutrientFilter(Nutrient.Protein, Min: 20)],
            MinRating = 1,
            Portions = 2,
        };

        // First call pays JIT, connection open and plan cache; the gate is
        // about steady-state latency, so it is warmed away.
        (await service.SearchAsync(query)).Total.Should().BeGreaterThan(0);

        var best = long.MaxValue;

        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            await service.SearchAsync(query);
            stopwatch.Stop();

            best = Math.Min(best, stopwatch.ElapsedMilliseconds);
        }

        best.Should().BeLessThan(GateMilliseconds,
            "over the gate, the designed fallback is due: v_recipe becomes a "
            + "materialized view refreshed at startup and after each normalise run");
    }
}
