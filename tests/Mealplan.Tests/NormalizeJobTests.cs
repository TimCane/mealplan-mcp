using FluentAssertions;
using Mealplan.Domain.Scraping;
using Mealplan.Infrastructure.Jobs;
using Mealplan.Infrastructure.Sources;
using Mealplan.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mealplan.Tests;

public class NormalizeJobTests
{
    private readonly InMemoryDocumentStore _documents = new();

    [Fact]
    public async Task Handled_documents_are_normalised_and_stamped()
    {
        await Store(DocumentType.Recipe, "a");
        await Store(DocumentType.Recipe, "b");

        var normalizer = new FakeNormalizer("fake", DocumentType.Recipe);

        var result = await CreateJob(normalizer).RunAsync("fake");

        result.Normalized.Should().Be(2);
        normalizer.Normalized.Select(d => d.SourceKey).Should().BeEquivalentTo("a", "b");
        _documents.Documents.Should().OnlyContain(d => d.NormalizedAt != null);
    }

    [Fact]
    public async Task Unhandled_document_types_are_stamped_rather_than_left_pending()
    {
        await Store(DocumentType.RecipeSummary, "list-page-1");

        var normalizer = new FakeNormalizer("fake", DocumentType.Recipe);

        var result = await CreateJob(normalizer).RunAsync("fake");

        result.Skipped.Should().Be(1);
        normalizer.Normalized.Should().BeEmpty();
        _documents.Documents.Single().NormalizedAt.Should()
            .NotBeNull("otherwise list pages would be re-examined on every pass, forever");
    }

    [Fact]
    public async Task A_failing_document_records_its_error_and_does_not_block_the_batch()
    {
        await Store(DocumentType.Recipe, "good-1");
        await Store(DocumentType.Recipe, "poison");
        await Store(DocumentType.Recipe, "good-2");

        var normalizer = new FakeNormalizer("fake", DocumentType.Recipe)
        {
            Fails = d => d.SourceKey == "poison" ? new InvalidOperationException("bad payload") : null,
        };

        var result = await CreateJob(normalizer).RunAsync("fake");

        result.Normalized.Should().Be(2);
        result.Failed.Should().Be(1);

        var poison = _documents.Documents.Single(d => d.SourceKey == "poison");
        poison.NormalizeError.Should().Contain("bad payload");
        poison.NormalizedAt.Should().NotBeNull("a poison payload must not spin the queue forever");
    }

    [Fact]
    public async Task Other_sources_are_left_alone()
    {
        await Store(DocumentType.Recipe, "mine");
        await _documents.StoreAsync(
            new RawDocument("other", DocumentType.Recipe, "theirs", "{}"),
            Guid.CreateVersion7());

        var normalizer = new FakeNormalizer("fake", DocumentType.Recipe);

        await CreateJob(normalizer).RunAsync("fake");

        normalizer.Normalized.Should().OnlyContain(d => d.Source == "fake");
        _documents.Documents.Single(d => d.Source == "other").NormalizedAt.Should().BeNull();
    }

    private Task Store(DocumentType type, string key) =>
        _documents.StoreAsync(new RawDocument("fake", type, key, "{}"), Guid.CreateVersion7());

    private NormalizeJob CreateJob(FakeNormalizer normalizer)
    {
        var registry = new SourceRegistry(
            [new FakeCrawler("fake", [])],
            [normalizer],
            [new FakeSchema()]);

        return new NormalizeJob(registry, _documents, NullLogger<NormalizeJob>.Instance);
    }
}
