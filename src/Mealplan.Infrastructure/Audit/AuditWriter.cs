using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Mealplan.Infrastructure.Audit;

/// <summary>
/// Drains the queue into the audit tables. Every failure is logged and
/// swallowed: the rows are droppable analytics, and nothing on this path may
/// ever surface as a request error.
/// </summary>
public class AuditWriter(
    AuditQueue queue,
    IServiceScopeFactory scopes,
    ILogger<AuditWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await WriteAsync(entry, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Dropped audit row for {Kind} {Name}", entry.Call.Kind, entry.Call.Name);
            }
        }
    }

    /// <summary>
    /// One round trip: the session upsert rides with every call and the first
    /// contact wins, so a session row exists however many calls follow and no
    /// in-memory registry has to survive restarts.
    /// </summary>
    private async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ScrapeDbContext>();
        var (session, call) = entry;

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO audit.session
                (id, first_seen_at, client_name, client_version, user_agent, ip_hash)
            VALUES
                (@session_id, @called_at, @client_name, @client_version, @user_agent, @ip_hash)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO audit.tool_call
                (id, session_id, kind, name, args, called_at,
                 duration_ms, result_count, is_error, error_kind)
            VALUES
                (@id, @session_id, @kind, @name, @args, @called_at,
                 @duration_ms, @result_count, @is_error, @error_kind);
            """,
            new object[]
            {
                new NpgsqlParameter("session_id", session.Id),
                new NpgsqlParameter("client_name", Nullable(session.ClientName)),
                new NpgsqlParameter("client_version", Nullable(session.ClientVersion)),
                new NpgsqlParameter("user_agent", Nullable(session.UserAgent)),
                new NpgsqlParameter("ip_hash", Nullable(session.IpHash)),
                new NpgsqlParameter("id", call.Id),
                new NpgsqlParameter("kind", call.Kind == AuditCallKind.Prompt ? "prompt" : "tool"),
                new NpgsqlParameter("name", call.Name),
                new NpgsqlParameter("args", NpgsqlDbType.Jsonb) { Value = Nullable(call.ArgsJson) },
                new NpgsqlParameter("called_at", call.CalledAt),
                new NpgsqlParameter("duration_ms", call.DurationMs),
                new NpgsqlParameter("result_count", Nullable(call.ResultCount)),
                new NpgsqlParameter("is_error", call.IsError),
                new NpgsqlParameter("error_kind", Nullable(call.ErrorKind)),
            },
            ct);
    }

    private static object Nullable<T>(T? value) => value is null ? DBNull.Value : value;
}
