using Mealplan.Domain.Scraping;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>scrape</c> schema. Source-agnostic on purpose: adding a recipe
/// source adds no tables here, only rows with a different <c>source</c> value.
/// </summary>
public class ScrapeDbContext(DbContextOptions<ScrapeDbContext> options) : DbContext(options)
{
    public const string SchemaName = "scrape";

    public DbSet<ScrapeRun> Runs => Set<ScrapeRun>();

    public DbSet<ScrapeDocument> Documents => Set<ScrapeDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScrapeDbContext).Assembly);
    }
}
