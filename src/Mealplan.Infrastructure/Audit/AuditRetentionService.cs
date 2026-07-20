using Mealplan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Mealplan.Infrastructure.Audit;

/// <summary>
/// Deletes audit rows past retention, at startup and then daily. A session
/// row goes once it is past retention and its last call has gone with it.
/// </summary>
public class AuditRetentionService(
    IServiceScopeFactory scopes,
    IOptions<AuditOptions> options,
    TimeProvider time,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), time);

        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var db = scope.ServiceProvider.GetRequiredService<ScrapeDbContext>();
                var cutoff = time.GetUtcNow().AddDays(-options.Value.RetentionDays);
                var deleted = await SweepAsync(db, cutoff, stoppingToken);

                logger.LogInformation(
                    "Audit sweep removed {Rows} rows older than {Cutoff}", deleted, cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Audit retention sweep failed; retrying tomorrow");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task<int> SweepAsync(
        ScrapeDbContext db,
        DateTimeOffset cutoff,
        CancellationToken ct = default)
    {
        var calls = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM audit.tool_call WHERE called_at < @cutoff",
            new object[] { new NpgsqlParameter("cutoff", cutoff) },
            ct);

        var sessions = await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM audit.session s
            WHERE s.first_seen_at < @cutoff
              AND NOT EXISTS (SELECT 1 FROM audit.tool_call c WHERE c.session_id = s.id)
            """,
            new object[] { new NpgsqlParameter("cutoff", cutoff) },
            ct);

        return calls + sessions;
    }
}
