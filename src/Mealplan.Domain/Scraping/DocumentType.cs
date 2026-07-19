namespace Mealplan.Domain.Scraping;

/// <summary>
/// What a scraped document holds. Sources vary: HelloFresh search pages return
/// complete recipes, while Gousto list pages are thin and only carry enough to
/// discover slugs and detect change.
/// </summary>
public enum DocumentType
{
    /// <summary>A list-page entry. Cheap to refetch, used for change detection.</summary>
    RecipeSummary = 1,

    /// <summary>A complete recipe payload.</summary>
    Recipe = 2,

    /// <summary>Categories, cuisines, themes and the like.</summary>
    Taxonomy = 3,
}
