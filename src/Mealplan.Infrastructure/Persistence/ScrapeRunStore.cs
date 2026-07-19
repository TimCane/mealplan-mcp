using Mealplan.Domain.Scraping;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.Persistence;

public class ScrapeRunStore(ScrapeDbContext db, TimeProvider clock) : IScrapeRunStore
{
    public async Task<ScrapeRun> StartAsync(string source, CancellationToken ct = default)
    {
        var run = new ScrapeRun
        {
            Id = Guid.CreateVersion7(),
            Source = source,
            Status = ScrapeRunStatus.Running,
            StartedAt = clock.GetUtcNow(),
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task SaveCursorAsync(Guid runId, string cursor, CancellationToken ct = default)
    {
        await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Cursor, cursor), ct);
    }

    public async Task CompleteAsync(
        Guid runId,
        ScrapeRunStatus status,
        int documentsFetched,
        int documentsChanged,
        string? error = null,
        CancellationToken ct = default)
    {
        var finishedAt = clock.GetUtcNow();

        await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.FinishedAt, finishedAt)
                    .SetProperty(r => r.DocumentsFetched, documentsFetched)
                    .SetProperty(r => r.DocumentsChanged, documentsChanged)
                    .SetProperty(r => r.Error, error),
                ct);
    }

    public async Task<ScrapeRun?> GetLatestAsync(string source, CancellationToken ct = default)
    {
        return await db.Runs
            .Where(r => r.Source == source)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CancelAbandonedAsync(CancellationToken ct = default)
    {
        var finishedAt = clock.GetUtcNow();

        // Cancelled rather than Failed: the cursor is intact, so this reads as an
        // interrupted run and the next crawl resumes from it.
        return await db.Runs
            .Where(r => r.Status == ScrapeRunStatus.Running)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, ScrapeRunStatus.Cancelled)
                    .SetProperty(r => r.FinishedAt, finishedAt)
                    .SetProperty(r => r.Error, "The scraper restarted while this run was in flight."),
                ct);
    }
}
