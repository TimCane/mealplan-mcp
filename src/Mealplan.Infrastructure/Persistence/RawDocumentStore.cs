using Mealplan.Domain.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Crawls run one at a time per source, so the read-then-insert below does not
/// need locking. The unique index on (source, type, key, version) is the
/// backstop if that ever stops being true.
/// </summary>
public class RawDocumentStore(
    ScrapeDbContext db,
    TimeProvider clock,
    ILogger<RawDocumentStore> logger) : IRawDocumentStore
{
    public async Task<StoreOutcome> StoreAsync(
        RawDocument document,
        Guid runId,
        CancellationToken ct = default)
    {
        var hash = ContentHash.Compute(document.Payload);
        var now = clock.GetUtcNow();

        var latest = await db.Documents
            .Where(d => d.Source == document.Source
                && d.DocumentType == document.DocumentType
                && d.SourceKey == document.SourceKey)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && latest.ContentHash.SequenceEqual(hash))
        {
            latest.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
            return StoreOutcome.Unchanged;
        }

        var version = latest is null ? 1 : latest.Version + 1;

        db.Documents.Add(new ScrapeDocument
        {
            Id = Guid.CreateVersion7(),
            Source = document.Source,
            DocumentType = document.DocumentType,
            SourceKey = document.SourceKey,
            Version = version,
            Payload = document.Payload,
            ContentHash = hash,
            FirstSeenAt = now,
            LastSeenAt = now,
            RunId = runId,
        });

        await db.SaveChangesAsync(ct);

        logger.LogDebug(
            "Stored {Source}/{DocumentType}/{SourceKey} version {Version}",
            document.Source,
            document.DocumentType,
            document.SourceKey,
            version);

        return latest is null ? StoreOutcome.Inserted : StoreOutcome.Versioned;
    }

    public async Task MarkNormalizedAsync(
        Guid documentId,
        string? error = null,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        await db.Documents
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(d => d.NormalizedAt, now)
                    .SetProperty(d => d.NormalizeError, error),
                ct);
    }

    public async Task<IReadOnlyList<ScrapeDocument>> GetPendingNormalizationAsync(
        string source,
        int limit,
        CancellationToken ct = default)
    {
        return await db.Documents
            .Where(d => d.Source == source && d.NormalizedAt == null)
            .OrderBy(d => d.FirstSeenAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
