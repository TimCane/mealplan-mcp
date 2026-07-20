using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Brings the materialized v_recipe up to date with the normalised tables.
/// An interface so the normalise pipeline can be unit tested without Postgres.
/// </summary>
public interface IRecipeViewRefresher
{
    Task RefreshAsync(CancellationToken ct = default);
}

public class RecipeViewRefresher(ScrapeDbContext db) : IRecipeViewRefresher
{
    /// <summary>
    /// Guarded: the scraper host refreshes but never builds the view, so on a
    /// database the MCP host has not touched yet there is nothing to refresh.
    /// ANALYZE rides along because the planner chose catastrophic plans off
    /// stale statistics.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$ BEGIN
                IF EXISTS (SELECT FROM pg_matviews
                           WHERE schemaname = 'public' AND matviewname = 'v_recipe') THEN
                    REFRESH MATERIALIZED VIEW public.v_recipe;
                    ANALYZE public.v_recipe;
                END IF;
            END $$;
            """,
            ct);
    }
}
