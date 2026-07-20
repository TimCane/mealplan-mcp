using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;

namespace Mealplan.Tests.Fakes;

/// <summary>
/// Stands in for a real source so the job and registry logic can be tested
/// without a network or a source project.
/// </summary>
public class FakeCrawler(string source, IReadOnlyList<CrawlItem> items) : ISourceCrawler
{
    public string Source { get; } = source;

    public CrawlRequest? LastRequest { get; private set; }

    public Exception? ThrowAfterYielding { get; set; }

    public async IAsyncEnumerable<CrawlItem> CrawlAsync(
        CrawlRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        LastRequest = request;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }

        if (ThrowAfterYielding is not null)
        {
            throw ThrowAfterYielding;
        }
    }
}

public class FakeNormalizer(string source, params DocumentType[] handles) : ISourceNormalizer
{
    public string Source { get; } = source;

    public IReadOnlySet<DocumentType> Handles { get; } =
        handles.Length > 0 ? handles.ToHashSet() : [DocumentType.Recipe];

    public List<ScrapeDocument> Normalized { get; } = [];

    public Func<ScrapeDocument, Exception?>? Fails { get; set; }

    public Task NormalizeAsync(ScrapeDocument document, CancellationToken ct = default)
    {
        var failure = Fails?.Invoke(document);
        if (failure is not null)
        {
            throw failure;
        }

        Normalized.Add(document);
        return Task.CompletedTask;
    }
}

public class FakeSchema(string source = "fake") : ISourceSchema
{
    public string Source { get; } = source;

    public string DisplayName => "Fake";

    public string SchemaName => Source;

    public SourceCapabilities Capabilities { get; } = new(
        HasIngredientQuantities: true,
        HasPantryItems: false,
        HasNutrition: true,
        HasTraceAllergens: false,
        HasUtensils: false,
        [2, 4]);

    public string RecipeViewSql => "SELECT NULL WHERE false";

    public string RecipeIngredientViewSql => "SELECT NULL WHERE false";

    public string AllergenViewSql => "SELECT NULL WHERE false";

    public string CuisineViewSql => "SELECT NULL WHERE false";

    public string TagViewSql => "SELECT NULL WHERE false";

    public string IngredientViewSql => "SELECT NULL WHERE false";
}
