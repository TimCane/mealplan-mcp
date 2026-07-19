using Mealplan.Domain.Scraping;

namespace Mealplan.Tests.Fakes;

/// <summary>
/// The store's real behaviour is covered by integration tests against Postgres.
/// These stand-ins exist so the job logic can be tested on its own.
/// </summary>
public class InMemoryDocumentStore : IRawDocumentStore
{
    private readonly List<ScrapeDocument> _documents = [];

    public IReadOnlyList<ScrapeDocument> Documents => _documents;

    public Func<RawDocument, StoreOutcome> Outcome { get; set; } = _ => StoreOutcome.Inserted;

    public Task<StoreOutcome> StoreAsync(
        RawDocument document,
        Guid runId,
        CancellationToken ct = default)
    {
        var outcome = Outcome(document);

        _documents.Add(new ScrapeDocument
        {
            Id = Guid.CreateVersion7(),
            Source = document.Source,
            DocumentType = document.DocumentType,
            SourceKey = document.SourceKey,
            Version = 1,
            Payload = document.Payload,
            ContentHash = [],
            RunId = runId,
        });

        return Task.FromResult(outcome);
    }

    public Task<IReadOnlyList<ScrapeDocument>> GetPendingNormalizationAsync(
        string source,
        int limit,
        CancellationToken ct = default)
    {
        IReadOnlyList<ScrapeDocument> pending = _documents
            .Where(d => d.Source == source && d.NormalizedAt is null)
            .Take(limit)
            .ToList();

        return Task.FromResult(pending);
    }

    public Task MarkNormalizedAsync(
        Guid documentId,
        string? error = null,
        CancellationToken ct = default)
    {
        var document = _documents.Single(d => d.Id == documentId);
        document.NormalizedAt = DateTimeOffset.UnixEpoch;
        document.NormalizeError = error;

        return Task.CompletedTask;
    }
}

public class InMemoryRunStore : IScrapeRunStore
{
    private readonly List<ScrapeRun> _runs = [];

    public IReadOnlyList<ScrapeRun> Runs => _runs;

    public List<string> SavedCursors { get; } = [];

    public Task<ScrapeRun> StartAsync(string source, CancellationToken ct = default)
    {
        var run = new ScrapeRun
        {
            Id = Guid.CreateVersion7(),
            Source = source,
            Status = ScrapeRunStatus.Running,
            StartedAt = DateTimeOffset.UnixEpoch,
        };

        _runs.Add(run);
        return Task.FromResult(run);
    }

    public Task SaveCursorAsync(Guid runId, string cursor, CancellationToken ct = default)
    {
        _runs.Single(r => r.Id == runId).Cursor = cursor;
        SavedCursors.Add(cursor);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(
        Guid runId,
        ScrapeRunStatus status,
        string? error = null,
        CancellationToken ct = default)
    {
        var run = _runs.Single(r => r.Id == runId);
        run.Status = status;
        run.Error = error;
        run.FinishedAt = DateTimeOffset.UnixEpoch;

        return Task.CompletedTask;
    }

    public Task<ScrapeRun?> GetLatestAsync(string source, CancellationToken ct = default) =>
        Task.FromResult(_runs.LastOrDefault(r => r.Source == source));

    public void Seed(ScrapeRun run) => _runs.Add(run);
}
