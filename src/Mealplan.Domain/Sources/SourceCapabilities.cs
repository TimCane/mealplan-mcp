namespace Mealplan.Domain.Sources;

/// <summary>
/// What a source can and cannot tell us. Reported through the MCP surface so a
/// calling model knows a null quantity means "this source does not publish
/// quantities", not "this recipe needs none of that ingredient".
/// </summary>
/// <param name="HasIngredientQuantities">
/// False for sources that ship boxes rather than quantities - Gousto lists SKUs
/// with no amounts.
/// </param>
/// <param name="HasPantryItems">
/// True when the source separates store-cupboard items you supply yourself from
/// the ingredients it sends.
/// </param>
/// <param name="HasNutrition">True when per-portion nutrition is published.</param>
/// <param name="PortionSizes">Portion counts the source publishes recipes for.</param>
public sealed record SourceCapabilities(
    bool HasIngredientQuantities,
    bool HasPantryItems,
    bool HasNutrition,
    IReadOnlyList<int> PortionSizes);
