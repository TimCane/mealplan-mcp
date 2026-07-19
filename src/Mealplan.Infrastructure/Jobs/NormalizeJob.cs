using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Sources;
using Microsoft.Extensions.Logging;

namespace Mealplan.Infrastructure.Jobs;

/// <summary>
/// Turns stored payloads into a source's normalised tables. Separate from
/// crawling on purpose: a mapping fix replays over what is already stored,
/// without going back to the network.
/// </summary>
public class NormalizeJob(
    SourceRegistry registry,
    IRawDocumentStore documents,
    ILogger<NormalizeJob> logger)
{
    public async Task<NormalizeResult> RunAsync(
        string source,
        int batchSize = 200,
        CancellationToken ct = default)
    {
        var normalizer = registry.Normalizer(source);

        var normalized = 0;
        var skipped = 0;
        var failed = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await documents.GetPendingNormalizationAsync(source, batchSize, ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var document in batch)
            {
                if (!normalizer.Handles.Contains(document.DocumentType))
                {
                    // Stored for change detection only - Gousto list pages exist
                    // to discover slugs. Stamp it so it leaves the pending set.
                    await documents.MarkNormalizedAsync(document.Id, error: null, ct);
                    skipped++;
                    continue;
                }

                try
                {
                    await normalizer.NormalizeAsync(document, ct);
                    await documents.MarkNormalizedAsync(document.Id, error: null, ct);
                    normalized++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "Normalising {Source} document {DocumentId} ({SourceKey} v{Version}) failed",
                        source,
                        document.Id,
                        document.SourceKey,
                        document.Version);

                    await documents.MarkNormalizedAsync(document.Id, Truncate(ex.Message), ct);
                    failed++;
                }
            }
        }

        logger.LogInformation(
            "Normalised {Source}: {Normalized} done, {Skipped} not applicable, {Failed} failed",
            source,
            normalized,
            skipped,
            failed);

        return new NormalizeResult(normalized, skipped, failed);
    }

    private static string Truncate(string message) =>
        message.Length <= 4000 ? message : message[..4000];
}

public sealed record NormalizeResult(int Normalized, int Skipped, int Failed);
