using Mealplan.Infrastructure.Audit;
using Mealplan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using FluentAssertions;

namespace Mealplan.IntegrationTests;

/// <summary>
/// Drives the real server and reads back what the audit recorded. The rows
/// are analytics, so the properties that matter are that a session is one row
/// however many calls it makes, that a zero-result search is queryable as
/// such, and that nothing on this path can reach the caller.
/// </summary>
[Collection(CrossSourceCollection.Name)]
public sealed class AuditTests(CrossSourceViewFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private McpClient _client = null!;
    private ScrapeDbContext _db = null!;
    private DateTimeOffset _startedAt;

    public async Task InitializeAsync()
    {
        _db = fixture.ScrapeContext();
        _startedAt = DateTimeOffset.UtcNow;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Mealplan", fixture.ConnectionString));

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") },
            _factory.CreateClient(),
            ownsHttpClient: false);

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task One_session_row_carries_however_many_calls()
    {
        await _client.CallToolAsync("list_sources");
        await _client.CallToolAsync(
            "list_allergens",
            new Dictionary<string, object?> { ["take"] = 5 });
        await _client.GetPromptAsync("whats_available");

        // Completions are deliberately unlogged: per-keystroke volume,
        // partial typing, no insight.
        await _client.CompleteAsync(
            new PromptReference { Name = "plan_week" },
            argumentName: "allergens",
            argumentValue: "mu");

        var calls = await CallsAsync(3);

        calls.Select(c => c.Name).Should().Contain(
            ["list_sources", "list_allergens", "whats_available"]);
        calls.Should().ContainSingle(c => c.Kind == "prompt");
        calls.Should().NotContain(c => c.Name.Contains("complet", StringComparison.Ordinal));

        calls.Select(c => c.SessionId).Distinct().Should().ContainSingle(
            "one MCP session is one row however many calls it makes");

        var sessions = await SessionsAsync();
        sessions.Should().ContainSingle(s => s.Id == calls[0].SessionId)
            .Which.ClientName.Should().NotBeNullOrEmpty(
                "the initialize handshake names the client");
    }

    [Fact]
    public async Task A_search_that_finds_nothing_records_a_zero_row_page()
    {
        await _client.CallToolAsync(
            "search_recipes",
            new Dictionary<string, object?> { ["query"] = "zzzzznotarecipezzzzz" });

        var call = (await CallsAsync(1)).Single(c => c.Name == "search_recipes");

        call.ResultCount.Should().Be(0,
            "zero-result searches are the strongest surface-improvement signal, "
            + "so they must be queryable directly rather than inferred");
        call.IsError.Should().BeFalse();
        call.Args.Should().Contain("zzzzznotarecipezzzzz",
            "arguments are stored verbatim - the daily salt rotation is what "
            + "makes that safe");
    }

    [Fact]
    public async Task A_failed_call_records_the_error_rather_than_vanishing()
    {
        var miss = await _client.CallToolAsync(
            "get_recipe",
            new Dictionary<string, object?>
            {
                ["source"] = "hellofresh",
                ["recipeId"] = Guid.Empty,
                ["portions"] = 2,
            });

        miss.IsError.Should().NotBeTrue("an unknown id is a miss, not an error");

        var failed = await _client.CallToolAsync(
            "get_shopping_list",
            new Dictionary<string, object?>
            {
                ["recipes"] = new[]
                {
                    new { source = "gousto", recipeId = Guid.Empty, portions = 2 },
                },
            });

        failed.IsError.Should().BeTrue();

        var calls = await CallsAsync(2);

        calls.Single(c => c.Name == "get_recipe").ResultCount
            .Should().BeNull("get_recipe is hit or miss, not rows");

        var error = calls.Single(c => c.Name == "get_shopping_list");
        error.IsError.Should().BeTrue();
        error.ErrorKind.Should().NotBeNullOrEmpty(
            "the errors agents hit are the point of the audit, not noise in it");
    }

    /// <summary>
    /// The audit is fire-and-forget by construction, so a broken write must be
    /// invisible to the caller. Dropping the schema out from under the writer
    /// is the bluntest version of broken there is.
    /// </summary>
    [Fact]
    public async Task A_broken_audit_write_never_fails_the_call()
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("DROP SCHEMA audit CASCADE");

            var result = await _client.CallToolAsync("list_sources");

            result.IsError.Should().NotBeTrue();
            result.StructuredContent.Should().NotBeNull();
        }
        finally
        {
            await AuditSchema.EnsureAsync(_factory.Services);
        }
    }

    [Fact]
    public async Task The_sweep_deletes_only_rows_past_retention()
    {
        var session = $"retention-{Guid.CreateVersion7()}";
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(session, now.AddDays(-120));
        await InsertAsync(session, now.AddDays(-1));

        await AuditRetentionService.SweepAsync(_db, now.AddDays(-90));

        var remaining = await _db.Database
            .SqlQueryRaw<CallRow>(
                CallColumns + " FROM audit.tool_call WHERE session_id = {0}",
                session)
            .ToListAsync();

        remaining.Should().ContainSingle()
            .Which.CalledAt.Should().BeAfter(now.AddDays(-2));

        var sessions = await SessionsAsync();
        sessions.Should().Contain(s => s.Id == session,
            "a session keeps its row while any of its calls survive");
    }

    private async Task InsertAsync(string session, DateTimeOffset calledAt)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO audit.session (id, first_seen_at) VALUES (@session, @called_at)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO audit.tool_call
                (id, session_id, kind, name, called_at, duration_ms, is_error)
            VALUES (@id, @session, 'tool', 'search_recipes', @called_at, 1, false);
            """,
            new object[]
            {
                new NpgsqlParameter("session", session),
                new NpgsqlParameter("called_at", calledAt),
                new NpgsqlParameter("id", Guid.CreateVersion7()),
            });
    }

    /// <summary>
    /// The writer drains a queue in the background, so rows arrive shortly
    /// after the call returns - which is the point of it.
    /// </summary>
    private async Task<IReadOnlyList<CallRow>> CallsAsync(int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var rows = await _db.Database
                .SqlQueryRaw<CallRow>(
                    CallColumns
                        + " FROM audit.tool_call WHERE called_at >= {0} ORDER BY called_at",
                    _startedAt)
                .ToListAsync();

            if (rows.Count >= expected)
            {
                return rows;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Only saw fewer than {expected} audited calls.");
    }

    private async Task<IReadOnlyList<SessionRow>> SessionsAsync() =>
        await _db.Database
            .SqlQueryRaw<SessionRow>("SELECT id, client_name FROM audit.session")
            .ToListAsync();

    /// <summary>
    /// Columns come back under their own names: the context's snake-case
    /// convention maps them onto the record, and an alias only fights it.
    /// </summary>
    private const string CallColumns =
        "SELECT session_id, kind, name, args::text AS args, called_at, "
        + "result_count, is_error, error_kind";

    private sealed record CallRow(
        string SessionId,
        string Kind,
        string Name,
        string? Args,
        DateTimeOffset CalledAt,
        int? ResultCount,
        bool IsError,
        string? ErrorKind);

    private sealed record SessionRow(string Id, string? ClientName);
}
