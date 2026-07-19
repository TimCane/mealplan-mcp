using Mealplan.Domain.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mealplan.Infrastructure.Persistence.Configurations;

public class ScrapeRunConfiguration : IEntityTypeConfiguration<ScrapeRun>
{
    public void Configure(EntityTypeBuilder<ScrapeRun> builder)
    {
        builder.ToTable("run");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Source).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.Cursor).HasColumnType("jsonb");
        builder.Property(r => r.Error).HasMaxLength(4000);

        // "Latest run for this source" drives both resume and get_scrape_status.
        builder.HasIndex(r => new { r.Source, r.StartedAt });
    }
}
