using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Gousto.Persistence;

namespace Mealplan.Infrastructure.Gousto;

public class GoustoSchema : ISourceSchema
{
    public const string SourceSlug = "gousto";

    public string Source => SourceSlug;

    public string DisplayName => "Gousto";

    public string SchemaName => GoustoDbContext.SchemaName;

    /// <summary>
    /// Gousto ships boxes, not measured ingredients: there is no amount or unit
    /// anywhere in the payload, only SKU codes and how many go in the box. It
    /// does separate store-cupboard basics from what it sends.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: false,
        HasPantryItems: true,
        HasNutrition: true,
        PortionSizes: [1, 2, 3, 4, 5]);
}
