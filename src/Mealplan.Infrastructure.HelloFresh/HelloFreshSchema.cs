using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.HelloFresh.Persistence;

namespace Mealplan.Infrastructure.HelloFresh;

public class HelloFreshSchema : ISourceSchema
{
    public const string SourceSlug = "hellofresh";

    /// <summary>
    /// The token is read from the website, so it cannot use the API client that
    /// needs the token. Separate client, separate base address.
    /// </summary>
    public const string TokenClientName = "hellofresh-token";

    public string Source => SourceSlug;

    public string DisplayName => "HelloFresh";

    public string SchemaName => HelloFreshDbContext.SchemaName;

    /// <summary>
    /// HelloFresh publishes measured ingredients per portion count, which Gousto
    /// does not. It does not separate store-cupboard items - everything listed
    /// arrives in the box.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: true,
        HasPantryItems: false,
        HasNutrition: true,
        PortionSizes: [2, 3, 4]);
}
