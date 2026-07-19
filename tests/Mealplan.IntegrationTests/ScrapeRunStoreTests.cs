using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Mealplan.IntegrationTests;

public class ScrapeRunStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider _clock =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_run_left_in_flight_is_cancelled_with_its_cursor_intact()
    {
        // A killed process leaves its run Running forever. Seeding reads that as
        // a crawl already in progress and skips the source, so nothing ever
        // clears it and the source stops being crawled at all.
        var store = CreateStore(out var db);
        var source = NewSource();

        var run = await store.StartAsync(source);
        await store.SaveCursorAsync(run.Id, """{"offset":48}""");

        var cancelled = await store.CancelAbandonedAsync();

        cancelled.Should().BeGreaterThanOrEqualTo(1);

        // Untracked: the store writes with ExecuteUpdate, which the change
        // tracker knows nothing about, so a tracked read returns the stale entity.
        var reloaded = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        reloaded.Status.Should().Be(ScrapeRunStatus.Cancelled);
        reloaded.FinishedAt.Should().NotBeNull();
        // Compared loosely: the column is jsonb, so Postgres hands the text back
        // reformatted rather than byte-for-byte as it was written.
        reloaded.Cursor.Should().Contain(
            "48",
            "the next crawl resumes from where this one was interrupted");
    }

    [Fact]
    public async Task A_finished_run_is_left_alone()
    {
        var store = CreateStore(out var db);
        var source = NewSource();

        var run = await store.StartAsync(source);
        await store.CompleteAsync(run.Id, ScrapeRunStatus.Succeeded, 12, 3);

        await store.CancelAbandonedAsync();

        // Untracked: the store writes with ExecuteUpdate, which the change
        // tracker knows nothing about, so a tracked read returns the stale entity.
        var reloaded = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        reloaded.Status.Should().Be(ScrapeRunStatus.Succeeded);
        reloaded.Error.Should().BeNull();
    }

    [Fact]
    public async Task The_latest_run_for_a_source_is_the_most_recently_started()
    {
        var store = CreateStore(out _);
        var source = NewSource();

        var first = await store.StartAsync(source);
        await store.CompleteAsync(first.Id, ScrapeRunStatus.Succeeded, 1, 1);

        _clock.Advance(TimeSpan.FromHours(1));
        var second = await store.StartAsync(source);

        var latest = await store.GetLatestAsync(source);

        latest!.Id.Should().Be(second.Id);
    }

    // Runs are not scoped per test, and CancelAbandonedAsync sweeps the table, so
    // each test works under its own source name.
    private static string NewSource() => $"source-{Guid.NewGuid():N}";

    private ScrapeRunStore CreateStore(out ScrapeDbContext db)
    {
        db = postgres.CreateContext();
        return new ScrapeRunStore(db, _clock);
    }
}
