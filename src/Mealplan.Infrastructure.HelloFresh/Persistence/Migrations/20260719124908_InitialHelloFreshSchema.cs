using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mealplan.Infrastructure.HelloFresh.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHelloFreshSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hellofresh");

            migrationBuilder.CreateTable(
                name: "allergen",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allergen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cuisine",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuisine", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingredient",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    slug = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    family = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredient", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tag",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tag", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "utensil",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utensil", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recipe",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    slug = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    headline = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    difficulty = table.Column<int>(type: "integer", nullable: true),
                    prep_minutes = table.Column<int>(type: "integer", nullable: true),
                    total_minutes = table.Column<int>(type: "integer", nullable: true),
                    serving_size_grams = table.Column<double>(type: "double precision", nullable: true),
                    average_rating = table.Column<double>(type: "double precision", nullable: true),
                    ratings_count = table.Column<int>(type: "integer", nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    website_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "hellofresh",
                        principalTable: "category",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "recipe_allergen",
                schema: "hellofresh",
                columns: table => new
                {
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allergen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    traces_of = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_allergen", x => new { x.recipe_id, x.allergen_id });
                    table.ForeignKey(
                        name: "fk_recipe_allergen_allergen_allergen_id",
                        column: x => x.allergen_id,
                        principalSchema: "hellofresh",
                        principalTable: "allergen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_allergen_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_cuisine",
                schema: "hellofresh",
                columns: table => new
                {
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuisine_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_cuisine", x => new { x.recipe_id, x.cuisine_id });
                    table.ForeignKey(
                        name: "fk_recipe_cuisine_cuisine_cuisine_id",
                        column: x => x.cuisine_id,
                        principalSchema: "hellofresh",
                        principalTable: "cuisine",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_cuisine_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_nutrition",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    amount = table.Column<double>(type: "double precision", nullable: true),
                    unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_nutrition", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_nutrition_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_step",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    instructions = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_step_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_tag",
                schema: "hellofresh",
                columns: table => new
                {
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_tag", x => new { x.recipe_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_recipe_tag_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_tag_tag_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "hellofresh",
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_utensil",
                schema: "hellofresh",
                columns: table => new
                {
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    utensil_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_utensil", x => new { x.recipe_id, x.utensil_id });
                    table.ForeignKey(
                        name: "fk_recipe_utensil_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_utensil_utensil_utensil_id",
                        column: x => x.utensil_id,
                        principalSchema: "hellofresh",
                        principalTable: "utensil",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_yield",
                schema: "hellofresh",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_yield", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_yield_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_yield_ingredient",
                schema: "hellofresh",
                columns: table => new
                {
                    yield_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<double>(type: "double precision", nullable: true),
                    unit = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_yield_ingredient", x => new { x.yield_id, x.ingredient_id });
                    table.ForeignKey(
                        name: "fk_recipe_yield_ingredient_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "hellofresh",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipe_yield_ingredient_recipe_yield_yield_id",
                        column: x => x.yield_id,
                        principalSchema: "hellofresh",
                        principalTable: "recipe_yield",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allergen_external_id",
                schema: "hellofresh",
                table: "allergen",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_external_id",
                schema: "hellofresh",
                table: "category",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cuisine_external_id",
                schema: "hellofresh",
                table: "cuisine",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_external_id",
                schema: "hellofresh",
                table: "ingredient",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_category_id",
                schema: "hellofresh",
                table: "recipe",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_external_id",
                schema: "hellofresh",
                table: "recipe",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_slug",
                schema: "hellofresh",
                table: "recipe",
                column: "slug");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_allergen_allergen_id",
                schema: "hellofresh",
                table: "recipe_allergen",
                column: "allergen_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_cuisine_cuisine_id",
                schema: "hellofresh",
                table: "recipe_cuisine",
                column: "cuisine_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_nutrition_recipe_id_name",
                schema: "hellofresh",
                table: "recipe_nutrition",
                columns: new[] { "recipe_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_step_recipe_id_index",
                schema: "hellofresh",
                table: "recipe_step",
                columns: new[] { "recipe_id", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_tag_tag_id",
                schema: "hellofresh",
                table: "recipe_tag",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_utensil_utensil_id",
                schema: "hellofresh",
                table: "recipe_utensil",
                column: "utensil_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_yield_recipe_id_portions",
                schema: "hellofresh",
                table: "recipe_yield",
                columns: new[] { "recipe_id", "portions" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_yield_ingredient_ingredient_id",
                schema: "hellofresh",
                table: "recipe_yield_ingredient",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_tag_external_id",
                schema: "hellofresh",
                table: "tag",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_utensil_external_id",
                schema: "hellofresh",
                table: "utensil",
                column: "external_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recipe_allergen",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_cuisine",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_nutrition",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_step",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_tag",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_utensil",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_yield_ingredient",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "allergen",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "cuisine",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "tag",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "utensil",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "ingredient",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe_yield",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "recipe",
                schema: "hellofresh");

            migrationBuilder.DropTable(
                name: "category",
                schema: "hellofresh");
        }
    }
}
