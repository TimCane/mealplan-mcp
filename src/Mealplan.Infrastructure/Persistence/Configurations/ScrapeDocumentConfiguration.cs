using Mealplan.Domain.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mealplan.Infrastructure.Persistence.Configurations;

public class ScrapeDocumentConfiguration : IEntityTypeConfiguration<ScrapeDocument>
{
    public void Configure(EntityTypeBuilder<ScrapeDocument> builder)
    {
        builder.ToTable("document");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Source).HasMaxLength(64).IsRequired();
        builder.Property(d => d.SourceKey).HasMaxLength(512).IsRequired();
        builder.Property(d => d.DocumentType).IsRequired();
        builder.Property(d => d.Version).IsRequired();
        builder.Property(d => d.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.ContentHash).HasColumnType("bytea").IsRequired();
        builder.Property(d => d.NormalizeError).HasMaxLength(4000);

        // One row per version of a document.
        builder
            .HasIndex(d => new { d.Source, d.DocumentType, d.SourceKey, d.Version })
            .IsUnique();

        // The normalise job's only query: pending work for one source, oldest
        // first. Partial, because normalised rows are the overwhelming majority.
        builder
            .HasIndex(d => new { d.Source, d.FirstSeenAt })
            .HasFilter("normalized_at IS NULL")
            .HasDatabaseName("ix_document_pending_normalization");

        builder.HasIndex(d => d.RunId);
    }
}
