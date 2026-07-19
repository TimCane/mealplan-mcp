using System.Text.Json.Serialization;

namespace Mealplan.Infrastructure.Gousto.Api;

// Shapes taken from production-api.gousto.co.uk/cmsreadbroker/v1. Only the parts
// worth normalising are modelled; the full payload is kept as jsonb regardless,
// so anything omitted here can be picked up later without re-crawling.

public sealed record GoustoListResponse(
    [property: JsonPropertyName("data")] GoustoListData? Data,
    [property: JsonPropertyName("meta")] GoustoListMeta? Meta);

public sealed record GoustoListData(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("entries")] IReadOnlyList<GoustoListEntry>? Entries);

public sealed record GoustoListMeta(
    [property: JsonPropertyName("skip")] int Skip,
    [property: JsonPropertyName("limit")] int Limit);

/// <summary>
/// List pages carry only enough to find a recipe. The slug in <see cref="Url"/>
/// is what the detail endpoint takes.
/// </summary>
public sealed record GoustoListEntry(
    [property: JsonPropertyName("uid")] string? Uid,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url);

public sealed record GoustoDetailResponse(
    [property: JsonPropertyName("data")] GoustoDetailData? Data);

public sealed record GoustoDetailData(
    [property: JsonPropertyName("entry")] GoustoRecipe? Entry);

public sealed record GoustoRecipe(
    [property: JsonPropertyName("uid")] string? Uid,
    [property: JsonPropertyName("gousto_uid")] string? GoustoUid,
    [property: JsonPropertyName("gousto_id")] string? GoustoId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("rating")] GoustoRating? Rating,
    [property: JsonPropertyName("cuisine")] GoustoTaxon? Cuisine,
    [property: JsonPropertyName("categories")] IReadOnlyList<GoustoCategory>? Categories,
    [property: JsonPropertyName("allergens")] IReadOnlyList<GoustoTaxon>? Allergens,
    [property: JsonPropertyName("basics")] IReadOnlyList<GoustoTaxon>? Basics,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<GoustoIngredient>? Ingredients,
    [property: JsonPropertyName("portion_sizes")] IReadOnlyList<GoustoPortionSize>? PortionSizes,
    [property: JsonPropertyName("prep_times")] GoustoPrepTimes? PrepTimes,
    [property: JsonPropertyName("cooking_instructions")] IReadOnlyList<GoustoInstruction>? CookingInstructions,
    [property: JsonPropertyName("nutritional_information")] GoustoNutritionalInformation? Nutrition,
    [property: JsonPropertyName("media")] GoustoMedia? Media);

public sealed record GoustoRating(
    [property: JsonPropertyName("average")] double? Average,
    [property: JsonPropertyName("count")] int? Count);

public sealed record GoustoTaxon(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("slug")] string? Slug);

public sealed record GoustoCategory(
    [property: JsonPropertyName("uid")] string? Uid,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url);

/// <summary>
/// An ingredient as Gousto ships it. There is no amount or unit - only a label,
/// which sometimes carries a weight in free text ("... (5.5g)") and sometimes
/// does not ("... x0"). The label is stored verbatim; parsing it would be
/// guesswork dressed up as data.
/// </summary>
public sealed record GoustoIngredient(
    [property: JsonPropertyName("gousto_uuid")] string? GoustoUuid,
    [property: JsonPropertyName("uid")] string? Uid,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("allergens")] GoustoIngredientAllergens? Allergens);

public sealed record GoustoIngredientAllergens(
    [property: JsonPropertyName("allergen")] IReadOnlyList<GoustoTaxon>? Allergen);

public sealed record GoustoPortionSize(
    [property: JsonPropertyName("portions")] int Portions,
    [property: JsonPropertyName("is_offered")] bool IsOffered,
    [property: JsonPropertyName("ingredients_skus")] IReadOnlyList<GoustoSku>? IngredientSkus);

/// <summary><see cref="Id"/> joins to <see cref="GoustoIngredient.GoustoUuid"/>.</summary>
public sealed record GoustoSku(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("quantities")] GoustoSkuQuantities? Quantities);

public sealed record GoustoSkuQuantities(
    [property: JsonPropertyName("in_box")] int? InBox);

/// <summary>Prep time varies by portion count, and only 2 and 4 are published.</summary>
public sealed record GoustoPrepTimes(
    [property: JsonPropertyName("for_2")] int? For2,
    [property: JsonPropertyName("for_4")] int? For4);

public sealed record GoustoInstruction(
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("instruction")] string? Instruction);

public sealed record GoustoNutritionalInformation(
    [property: JsonPropertyName("per_portion")] GoustoNutrition? PerPortion,
    [property: JsonPropertyName("per_hundred_grams")] GoustoNutrition? PerHundredGrams);

public sealed record GoustoNutrition(
    [property: JsonPropertyName("energy_kcal")] double? EnergyKcal,
    [property: JsonPropertyName("energy_kj")] double? EnergyKj,
    [property: JsonPropertyName("fat_mg")] double? FatMg,
    [property: JsonPropertyName("fat_saturates_mg")] double? SaturatedFatMg,
    [property: JsonPropertyName("carbs_mg")] double? CarbsMg,
    [property: JsonPropertyName("carbs_sugars_mg")] double? SugarsMg,
    [property: JsonPropertyName("fibre_mg")] double? FibreMg,
    [property: JsonPropertyName("protein_mg")] double? ProteinMg,
    [property: JsonPropertyName("salt_mg")] double? SaltMg,
    [property: JsonPropertyName("net_weight_mg")] double? NetWeightMg);

public sealed record GoustoMedia(
    [property: JsonPropertyName("images")] IReadOnlyList<GoustoImage>? Images);

public sealed record GoustoImage(
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("width")] int? Width);
