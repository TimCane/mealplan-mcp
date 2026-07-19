using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mealplan.Infrastructure.Gousto.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGoustoSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gousto");

            migrationBuilder.CreateTable(
                name: "allergen",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allergen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cuisine",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuisine", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingredient",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gousto_uuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredient", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recipe",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    gousto_uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    gousto_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    rating_average = table.Column<double>(type: "double precision", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    cuisine_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_cuisines_cuisine_id",
                        column: x => x.cuisine_id,
                        principalSchema: "gousto",
                        principalTable: "cuisine",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "pantry_item",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pantry_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_pantry_item_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "gousto",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_allergen",
                schema: "gousto",
                columns: table => new
                {
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allergen_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_allergen", x => new { x.recipe_id, x.allergen_id });
                    table.ForeignKey(
                        name: "fk_recipe_allergen_allergen_allergen_id",
                        column: x => x.allergen_id,
                        principalSchema: "gousto",
                        principalTable: "allergen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_allergen_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "gousto",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_category",
                schema: "gousto",
                columns: table => new
                {
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_category", x => new { x.recipe_id, x.category_id });
                    table.ForeignKey(
                        name: "fk_recipe_category_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "gousto",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_category_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "gousto",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_nutrition",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    energy_kcal = table.Column<double>(type: "double precision", nullable: true),
                    energy_kj = table.Column<double>(type: "double precision", nullable: true),
                    fat_grams = table.Column<double>(type: "double precision", nullable: true),
                    saturated_fat_grams = table.Column<double>(type: "double precision", nullable: true),
                    carbs_grams = table.Column<double>(type: "double precision", nullable: true),
                    sugars_grams = table.Column<double>(type: "double precision", nullable: true),
                    fibre_grams = table.Column<double>(type: "double precision", nullable: true),
                    protein_grams = table.Column<double>(type: "double precision", nullable: true),
                    salt_grams = table.Column<double>(type: "double precision", nullable: true),
                    net_weight_grams = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_nutrition", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_nutrition_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "gousto",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_step",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    instruction_html = table.Column<string>(type: "text", nullable: false),
                    instruction_text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_step_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "gousto",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_yield",
                schema: "gousto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portions = table.Column<int>(type: "integer", nullable: false),
                    prep_minutes = table.Column<int>(type: "integer", nullable: true),
                    is_offered = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_yield", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_yield_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "gousto",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_yield_ingredient",
                schema: "gousto",
                columns: table => new
                {
                    yield_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    in_box = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_yield_ingredient", x => new { x.yield_id, x.ingredient_id });
                    table.ForeignKey(
                        name: "fk_recipe_yield_ingredient_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "gousto",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_yield_ingredient_recipe_yield_yield_id",
                        column: x => x.yield_id,
                        principalSchema: "gousto",
                        principalTable: "recipe_yield",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allergen_slug",
                schema: "gousto",
                table: "allergen",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_uid",
                schema: "gousto",
                table: "category",
                column: "uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cuisine_slug",
                schema: "gousto",
                table: "cuisine",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_gousto_uuid",
                schema: "gousto",
                table: "ingredient",
                column: "gousto_uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pantry_item_recipe_id_slug",
                schema: "gousto",
                table: "pantry_item",
                columns: new[] { "recipe_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_cuisine_id",
                schema: "gousto",
                table: "recipe",
                column: "cuisine_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_slug",
                schema: "gousto",
                table: "recipe",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_allergen_allergen_id",
                schema: "gousto",
                table: "recipe_allergen",
                column: "allergen_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_category_category_id",
                schema: "gousto",
                table: "recipe_category",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_nutrition_recipe_id_basis",
                schema: "gousto",
                table: "recipe_nutrition",
                columns: new[] { "recipe_id", "basis" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_step_recipe_id_order",
                schema: "gousto",
                table: "recipe_step",
                columns: new[] { "recipe_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_yield_recipe_id_portions",
                schema: "gousto",
                table: "recipe_yield",
                columns: new[] { "recipe_id", "portions" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_yield_ingredient_ingredient_id",
                schema: "gousto",
                table: "recipe_yield_ingredient",
                column: "ingredient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pantry_item",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe_allergen",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe_category",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe_nutrition",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe_step",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe_yield_ingredient",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "allergen",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "category",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "ingredient",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe_yield",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "recipe",
                schema: "gousto");

            migrationBuilder.DropTable(
                name: "cuisine",
                schema: "gousto");
        }
    }
}
