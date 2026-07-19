namespace Mealplan.Infrastructure.HelloFresh;

/// <summary>
/// HelloFresh-specific crawl settings. Politeness and scheduling live in the
/// shared SourceOptions; this is only what other sources have no equivalent of.
/// </summary>
public class HelloFreshOptions
{
    public const string SectionName = "Sources:hellofresh";

    /// <summary>
    /// Box types to search within. Without this the endpoint also returns
    /// add-on products - loose fruit, yoghurt, a baguette - which are not meals
    /// and would pad a meal plan with things that are not recipes. Filtering
    /// takes the catalogue from about 35,000 items to about 24,500 recipes.
    /// </summary>
    public IReadOnlyList<string> Products { get; set; } =
    [
        "classic-box",
        "veggie-box",
        "meal-plan",
        "classic-plan",
    ];

    public string Country { get; set; } = "GB";

    public string Locale { get; set; } = "en-GB";
}
