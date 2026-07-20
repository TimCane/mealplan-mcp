using System.Text;
using FluentAssertions;
using Mealplan.Infrastructure.Reading;
using Npgsql;

namespace Mealplan.Tests;

/// <summary>
/// The nutrient clauses are the one place a caller-chosen value steers SQL
/// text, so the whitelist mapping is pinned here rather than trusted.
/// </summary>
public class RecipeQuerySqlTests
{
    [Fact]
    public void Every_nutrient_maps_to_exactly_one_view_column()
    {
        RecipeQueryService.NutrientColumns.Keys
            .Should().BeEquivalentTo(Enum.GetValues<Nutrient>(),
                "an unmapped member would throw on first use");

        RecipeQueryService.NutrientColumns.Values
            .Should().OnlyHaveUniqueItems()
            .And.OnlyContain(column => column.All(c => char.IsLetterOrDigit(c) || c == '_'),
                "column names are interpolated and must never carry SQL syntax");
    }

    [Fact]
    public void A_min_and_max_filter_emits_both_bounds_and_their_parameters()
    {
        var (sql, parameters) = Build(new NutrientFilter(Nutrient.Protein, Min: 30, Max: 50));

        sql.Should().Contain("r.protein_g IS NOT NULL")
            .And.Contain("r.protein_g >= @nutrient_min_0")
            .And.Contain("r.protein_g <= @nutrient_max_0");

        parameters.Select(p => (p.ParameterName, p.Value))
            .Should().BeEquivalentTo(new[]
            {
                ("nutrient_min_0", (object)30d),
                ("nutrient_max_0", (object)50d),
            });
    }

    [Fact]
    public void A_single_bound_still_excludes_recipes_with_no_published_value()
    {
        var (minOnly, _) = Build(new NutrientFilter(Nutrient.Kcal, Min: 400));
        minOnly.Should().Contain("r.kcal IS NOT NULL").And.NotContain("<=");

        var (maxOnly, _) = Build(new NutrientFilter(Nutrient.Salt, Max: 1.5));
        maxOnly.Should().Contain("r.salt_g IS NOT NULL").And.NotContain(">=");
    }

    [Fact]
    public void Filters_are_numbered_so_two_on_different_nutrients_do_not_collide()
    {
        var (sql, parameters) = Build(
            new NutrientFilter(Nutrient.Carbs, Max: 80),
            new NutrientFilter(Nutrient.Fibre, Min: 5));

        sql.Should().Contain("@nutrient_max_0").And.Contain("@nutrient_min_1");
        parameters.Should().HaveCount(2);
    }

    [Fact]
    public void No_filters_leave_the_where_clause_untouched()
    {
        var where = new StringBuilder("r.portions = @portions");
        RecipeQueryService.AppendNutrientFilters(where, [], null);
        RecipeQueryService.AppendNutrientFilters(where, [], []);

        where.ToString().Should().Be("r.portions = @portions");
    }

    [Theory]
    [InlineData(RecipeSort.Name, "r.name, r.source")]
    [InlineData(RecipeSort.Rating, "r.rating_avg DESC NULLS LAST, r.rating_count DESC NULLS LAST, r.name, r.source")]
    [InlineData(RecipeSort.Kcal, "r.kcal ASC NULLS LAST, r.name, r.source")]
    [InlineData(RecipeSort.Random, "random()")]
    public void Each_sort_maps_to_a_fixed_order_clause(RecipeSort sort, string expected)
    {
        RecipeQueryService.OrderBy(sort).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-42)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Any_integer_seed_folds_into_the_setseed_range(int seed)
    {
        RecipeQueryService.NormalizeSeed(seed).Should().BeInRange(-1, 1);
    }

    private static (string Sql, List<NpgsqlParameter> Parameters) Build(
        params NutrientFilter[] filters)
    {
        var where = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();

        RecipeQueryService.AppendNutrientFilters(where, parameters, filters);

        return (where.ToString(), parameters);
    }
}
