using System.Text.Json.Serialization;

namespace Mealplan.Infrastructure.HelloFresh.Api;

// Shapes taken from www.hellofresh.co.uk/gw/recipes/recipes/search. The search
// response carries complete recipes, so there is no detail endpoint to follow.

public sealed record HelloFreshSearchResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<HelloFreshRecipe>? Items,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("skip")] int Skip,
    [property: JsonPropertyName("take")] int Take,
    [property: JsonPropertyName("total")] int Total);

public sealed record HelloFreshRecipe(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("headline")] string? Headline,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("difficulty")] int? Difficulty,
    [property: JsonPropertyName("prepTime")] string? PrepTime,
    [property: JsonPropertyName("totalTime")] string? TotalTime,
    [property: JsonPropertyName("servingSize")] double? ServingSize,
    [property: JsonPropertyName("averageRating")] double? AverageRating,
    [property: JsonPropertyName("ratingsCount")] int? RatingsCount,
    [property: JsonPropertyName("imageLink")] string? ImageLink,
    [property: JsonPropertyName("websiteUrl")] string? WebsiteUrl,
    [property: JsonPropertyName("category")] HelloFreshTaxon? Category,
    [property: JsonPropertyName("cuisines")] IReadOnlyList<HelloFreshTaxon>? Cuisines,
    [property: JsonPropertyName("tags")] IReadOnlyList<HelloFreshTaxon>? Tags,
    [property: JsonPropertyName("allergens")] IReadOnlyList<HelloFreshAllergen>? Allergens,
    [property: JsonPropertyName("utensils")] IReadOnlyList<HelloFreshTaxon>? Utensils,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<HelloFreshIngredient>? Ingredients,
    [property: JsonPropertyName("yields")] IReadOnlyList<HelloFreshYield>? Yields,
    [property: JsonPropertyName("nutrition")] IReadOnlyList<HelloFreshNutrient>? Nutrition,
    [property: JsonPropertyName("steps")] IReadOnlyList<HelloFreshStep>? Steps);

public sealed record HelloFreshTaxon(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("type")] string? Type);

/// <summary>
/// <see cref="TracesOf"/> distinguishes "contains" from "may contain traces of",
/// which matters enough not to flatten.
/// </summary>
public sealed record HelloFreshAllergen(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("tracesOf")] bool TracesOf);

public sealed record HelloFreshIngredient(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("imageLink")] string? ImageLink,
    [property: JsonPropertyName("family")] HelloFreshTaxon? Family);

/// <summary>
/// One portion count and its measured ingredients. Unlike Gousto, HelloFresh
/// publishes a real amount and unit per ingredient per yield.
/// </summary>
public sealed record HelloFreshYield(
    [property: JsonPropertyName("yields")] int? Yields,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<HelloFreshYieldIngredient>? Ingredients);

public sealed record HelloFreshYieldIngredient(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("amount")] double? Amount,
    [property: JsonPropertyName("unit")] string? Unit);

public sealed record HelloFreshNutrient(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("amount")] double? Amount,
    [property: JsonPropertyName("unit")] string? Unit);

public sealed record HelloFreshStep(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("instructions")] string? Instructions,
    [property: JsonPropertyName("instructionsMarkdown")] string? InstructionsMarkdown);

/// <summary>
/// The anonymous bearer the recipe API requires, as embedded in the website's
/// page data.
/// </summary>
public sealed record HelloFreshServerAuth(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("expires_in")] long? ExpiresIn,
    [property: JsonPropertyName("token_type")] string? TokenType);
