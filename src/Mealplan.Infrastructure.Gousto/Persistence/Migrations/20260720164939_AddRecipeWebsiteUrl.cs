using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mealplan.Infrastructure.Gousto.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeWebsiteUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "website_url",
                schema: "gousto",
                table: "recipe",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            // The URL comes from payloads already stored, so returning gousto
            // documents to the pending set makes the next normalise run backfill
            // the column without a re-crawl. Guarded: the scrape schema belongs
            // to another context and may not exist when this one migrates alone.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF to_regclass('scrape.document') IS NOT NULL THEN
                        UPDATE scrape.document
                        SET normalized_at = NULL
                        WHERE source = 'gousto';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "website_url",
                schema: "gousto",
                table: "recipe");
        }
    }
}
