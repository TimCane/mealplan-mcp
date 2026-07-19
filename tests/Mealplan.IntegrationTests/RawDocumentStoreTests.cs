using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Mealplan.IntegrationTests;

public class RawDocumentStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid RunId = Guid.CreateVersion7();

    // Driven explicitly so "last seen moved" is a real assertion rather than a
    // bet on the wall clock ticking between two fast calls.
    private readonly FakeTimeProvider _clock =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Storing_an_unseen_document_inserts_version_one()
    {
        var store = CreateStore(out var db);
        var key = NewKey();

        var outcome = await store.StoreAsync(Document(key, """{"name":"Cottage Pie"}"""), RunId);

        outcome.Should().Be(StoreOutcome.Inserted);

        var stored = await Documents(db, key).SingleAsync();
        stored.Version.Should().Be(1);
        stored.NormalizedAt.Should().BeNull("a new version has not been normalised yet");
        stored.FirstSeenAt.Should().Be(stored.LastSeenAt);
    }

    [Fact]
    public async Task Restoring_an_identical_payload_moves_last_seen_and_writes_no_version()
    {
        var store = CreateStore(out var db);
        var key = NewKey();
        const string Payload = """{"name":"Spag Bol","prep":30}""";

        await store.StoreAsync(Document(key, Payload), RunId);
        var first = await Documents(db, key).SingleAsync();
        var firstSeen = first.LastSeenAt;

        _clock.Advance(TimeSpan.FromHours(1));
        var outcome = await store.StoreAsync(Document(key, Payload), RunId);

        outcome.Should().Be(StoreOutcome.Unchanged);

        var all = await Documents(db, key).ToListAsync();
        all.Should().ContainSingle("an unchanged payload must not create a version");
        all[0].LastSeenAt.Should().BeAfter(firstSeen);
        all[0].FirstSeenAt.Should().Be(first.FirstSeenAt);
    }

    [Fact]
    public async Task Restoring_a_changed_payload_appends_a_version()
    {
        var store = CreateStore(out var db);
        var key = NewKey();

        await store.StoreAsync(Document(key, """{"name":"Katsu","prep":25}"""), RunId);
        var outcome = await store.StoreAsync(Document(key, """{"name":"Katsu","prep":35}"""), RunId);

        outcome.Should().Be(StoreOutcome.Versioned);

        var all = await Documents(db, key).OrderBy(d => d.Version).ToListAsync();
        all.Should().HaveCount(2);
        all.Select(d => d.Version).Should().Equal(1, 2);
        all[1].Payload.Should().Contain("35");
    }

    [Fact]
    public async Task Insignificant_whitespace_is_not_a_change()
    {
        var store = CreateStore(out var db);
        var key = NewKey();

        await store.StoreAsync(Document(key, """{"name":"Chowder"}"""), RunId);
        var outcome = await store.StoreAsync(
            Document(key, "{\n  \"name\" :  \"Chowder\"\n}"),
            RunId);

        outcome.Should().Be(StoreOutcome.Unchanged);
        (await Documents(db, key).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_same_key_under_a_different_document_type_is_a_different_document()
    {
        var store = CreateStore(out var db);
        var key = NewKey();

        await store.StoreAsync(
            new RawDocument("gousto", DocumentType.RecipeSummary, key, """{"a":1}"""),
            RunId);
        var outcome = await store.StoreAsync(
            new RawDocument("gousto", DocumentType.Recipe, key, """{"a":1}"""),
            RunId);

        outcome.Should().Be(StoreOutcome.Inserted, "summary and detail are separate documents");
        (await db.Documents.Where(d => d.SourceKey == key).CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Pending_normalization_returns_only_unnormalised_versions_for_the_source()
    {
        var store = CreateStore(out var db);
        var mine = NewKey();

        await store.StoreAsync(Document(mine, """{"n":1}"""), RunId);
        await store.StoreAsync(
            new RawDocument("hellofresh", DocumentType.Recipe, NewKey(), """{"n":2}"""),
            RunId);

        var pending = await store.GetPendingNormalizationAsync("gousto", limit: 100);

        pending.Should().OnlyContain(d => d.Source == "gousto");
        pending.Should().Contain(d => d.SourceKey == mine);

        // Mark it done; it should drop out of the pending set.
        var document = await Documents(db, mine).SingleAsync();
        document.NormalizedAt = _clock.GetUtcNow();
        await db.SaveChangesAsync();

        var after = await store.GetPendingNormalizationAsync("gousto", limit: 100);
        after.Should().NotContain(d => d.SourceKey == mine);
    }

    [Fact]
    public async Task Payload_is_queryable_as_jsonb()
    {
        var store = CreateStore(out var db);
        var key = NewKey();

        await store.StoreAsync(Document(key, """{"title":"Laksa","serves":2}"""), RunId);

        // The ->> operator only works if the column really is jsonb, not text.
        var titles = await db.Database
            .SqlQuery<string>($"SELECT payload->>'title' FROM scrape.document WHERE source_key = {key}")
            .ToListAsync();

        titles.Should().Equal("Laksa");
    }

    private static RawDocument Document(string key, string payload) =>
        new("gousto", DocumentType.Recipe, key, payload);

    private static string NewKey() => $"recipe-{Guid.NewGuid():N}";

    private static IQueryable<ScrapeDocument> Documents(ScrapeDbContext db, string key) =>
        db.Documents.Where(d => d.SourceKey == key && d.DocumentType == DocumentType.Recipe);

    private RawDocumentStore CreateStore(out ScrapeDbContext db)
    {
        db = postgres.CreateContext();
        return new RawDocumentStore(db, _clock, NullLogger<RawDocumentStore>.Instance);
    }
}
