using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mealplan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialScrapeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scrape");

            migrationBuilder.CreateTable(
                name: "document",
                schema: "scrape",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    source_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    normalize_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "run",
                schema: "scrape",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    documents_fetched = table.Column<int>(type: "integer", nullable: false),
                    documents_changed = table.Column<int>(type: "integer", nullable: false),
                    cursor = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_pending_normalization",
                schema: "scrape",
                table: "document",
                columns: new[] { "source", "first_seen_at" },
                filter: "normalized_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_document_run_id",
                schema: "scrape",
                table: "document",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_source_document_type_source_key_version",
                schema: "scrape",
                table: "document",
                columns: new[] { "source", "document_type", "source_key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_run_source_started_at",
                schema: "scrape",
                table: "run",
                columns: new[] { "source", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document",
                schema: "scrape");

            migrationBuilder.DropTable(
                name: "run",
                schema: "scrape");
        }
    }
}
