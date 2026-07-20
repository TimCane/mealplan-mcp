using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure.Audit;

/// <summary>
/// Creates the audit schema idempotently at startup, alongside the views.
/// Not a migration: the MCP host owns these tables and the scraper never
/// touches them, so tying them to a source context's migration history would
/// couple the hosts for nothing.
/// </summary>
public static class AuditSchema
{
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ScrapeDbContext>();

        await db.Database.ExecuteSqlRawAsync(Ddl, ct);
    }

    private const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS audit;
        CREATE TABLE IF NOT EXISTS audit.session (
            id text PRIMARY KEY,
            first_seen_at timestamptz NOT NULL,
            client_name text,
            client_version text,
            user_agent text,
            ip_hash text
        );
        CREATE TABLE IF NOT EXISTS audit.tool_call (
            id uuid PRIMARY KEY,
            session_id text NOT NULL REFERENCES audit.session (id),
            kind text NOT NULL,
            name text NOT NULL,
            args jsonb,
            called_at timestamptz NOT NULL,
            duration_ms integer NOT NULL,
            result_count integer,
            is_error boolean NOT NULL,
            error_kind text
        );
        CREATE INDEX IF NOT EXISTS ix_audit_tool_call_called_at
            ON audit.tool_call (called_at);
        CREATE INDEX IF NOT EXISTS ix_audit_tool_call_session_id
            ON audit.tool_call (session_id);
        """;
}
