using Mealplan.Infrastructure.Jobs;

namespace Mealplan.Scraper;

/// <summary>
/// Runs one crawl or normalise pass and exits, instead of starting the server.
/// Exists so a first run against a live source can be capped to a handful of
/// documents - letting a scheduled job loose on 3,880 recipes is a poor way to
/// find out the credentials are wrong.
/// </summary>
/// <remarks>
/// Usage: crawl &lt;source&gt; [--max N] | normalize &lt;source&gt;
/// </remarks>
public sealed record OneShotCommand(string Verb, string Source, int? MaxDocuments)
{
    public static OneShotCommand? Parse(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("crawl" or "normalize"))
        {
            return null;
        }

        int? max = null;

        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i] is "--max" && int.TryParse(args[i + 1], out var parsed))
            {
                max = parsed;
            }
        }

        return new OneShotCommand(args[0], args[1], max);
    }

    public async Task<int> RunAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();

        try
        {
            if (Verb == "crawl")
            {
                logger.LogInformation(
                    "Crawling {Source}{Cap}",
                    Source,
                    MaxDocuments is null ? " (no limit)" : $" (at most {MaxDocuments} documents)");

                var run = await scope.ServiceProvider
                    .GetRequiredService<CrawlJob>()
                    .RunAsync(Source, MaxDocuments);

                logger.LogInformation(
                    "Crawl finished: {Fetched} fetched, {Changed} changed",
                    run.DocumentsFetched,
                    run.DocumentsChanged);
            }
            else
            {
                var result = await scope.ServiceProvider
                    .GetRequiredService<NormalizeJob>()
                    .RunAsync(Source);

                logger.LogInformation(
                    "Normalise finished: {Normalized} done, {Skipped} skipped, {Failed} failed",
                    result.Normalized,
                    result.Skipped,
                    result.Failed);
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Verb} of {Source} failed", Verb, Source);
            return 1;
        }
    }
}
