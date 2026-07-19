using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.HelloFresh.Persistence;

/// <summary>
/// Owns the <c>hellofresh</c> schema and its own migration history.
/// </summary>
public class HelloFreshDbContext(DbContextOptions<HelloFreshDbContext> options) : DbContext(options)
{
    public const string SchemaName = "hellofresh";

    public DbSet<HelloFreshRecipeEntity> Recipes => Set<HelloFreshRecipeEntity>();

    public DbSet<HelloFreshCategoryEntity> Categories => Set<HelloFreshCategoryEntity>();

    public DbSet<HelloFreshCuisineEntity> Cuisines => Set<HelloFreshCuisineEntity>();

    public DbSet<HelloFreshTagEntity> Tags => Set<HelloFreshTagEntity>();

    public DbSet<HelloFreshAllergenEntity> Allergens => Set<HelloFreshAllergenEntity>();

    public DbSet<HelloFreshUtensilEntity> Utensils => Set<HelloFreshUtensilEntity>();

    public DbSet<HelloFreshIngredientEntity> Ingredients => Set<HelloFreshIngredientEntity>();

    public DbSet<HelloFreshYieldEntity> Yields => Set<HelloFreshYieldEntity>();

    public DbSet<HelloFreshStepEntity> Steps => Set<HelloFreshStepEntity>();

    public DbSet<HelloFreshNutritionEntity> Nutrition => Set<HelloFreshNutritionEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);

        model.Entity<HelloFreshRecipeEntity>(e =>
        {
            e.ToTable("recipe");
            e.HasKey(r => r.Id);
            e.Property(r => r.ExternalId).HasMaxLength(128).IsRequired();
            e.Property(r => r.Slug).HasMaxLength(512).IsRequired();
            e.Property(r => r.Name).HasMaxLength(512).IsRequired();
            e.Property(r => r.Headline).HasMaxLength(1024);
            e.Property(r => r.ImageUrl).HasMaxLength(1024);
            e.Property(r => r.WebsiteUrl).HasMaxLength(1024);
            e.HasIndex(r => r.ExternalId).IsUnique();
            e.HasIndex(r => r.Slug);
            e.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId);
        });

        Lookup<HelloFreshCategoryEntity>(model, "category");
        Lookup<HelloFreshCuisineEntity>(model, "cuisine");
        Lookup<HelloFreshTagEntity>(model, "tag");
        Lookup<HelloFreshAllergenEntity>(model, "allergen");

        model.Entity<HelloFreshUtensilEntity>(e =>
        {
            e.ToTable("utensil");
            e.HasKey(u => u.Id);
            e.Property(u => u.ExternalId).HasMaxLength(128).IsRequired();
            e.Property(u => u.Name).HasMaxLength(256).IsRequired();
            e.HasIndex(u => u.ExternalId).IsUnique();
        });

        model.Entity<HelloFreshIngredientEntity>(e =>
        {
            e.ToTable("ingredient");
            e.HasKey(i => i.Id);
            e.Property(i => i.ExternalId).HasMaxLength(128).IsRequired();
            e.Property(i => i.Name).HasMaxLength(512).IsRequired();
            e.Property(i => i.Slug).HasMaxLength(512);
            e.Property(i => i.Family).HasMaxLength(256);
            e.Property(i => i.ImageUrl).HasMaxLength(1024);
            e.HasIndex(i => i.ExternalId).IsUnique();
        });

        model.Entity<HelloFreshRecipeCuisineEntity>(e =>
        {
            e.ToTable("recipe_cuisine");
            e.HasKey(x => new { x.RecipeId, x.CuisineId });
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId);
            e.HasOne(x => x.Cuisine).WithMany().HasForeignKey(x => x.CuisineId);
        });

        model.Entity<HelloFreshRecipeTagEntity>(e =>
        {
            e.ToTable("recipe_tag");
            e.HasKey(x => new { x.RecipeId, x.TagId });
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId);
            e.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId);
        });

        model.Entity<HelloFreshRecipeAllergenEntity>(e =>
        {
            e.ToTable("recipe_allergen");
            e.HasKey(x => new { x.RecipeId, x.AllergenId });
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId);
            e.HasOne(x => x.Allergen).WithMany().HasForeignKey(x => x.AllergenId);
        });

        model.Entity<HelloFreshRecipeUtensilEntity>(e =>
        {
            e.ToTable("recipe_utensil");
            e.HasKey(x => new { x.RecipeId, x.UtensilId });
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId);
            e.HasOne(x => x.Utensil).WithMany().HasForeignKey(x => x.UtensilId);
        });

        model.Entity<HelloFreshYieldEntity>(e =>
        {
            e.ToTable("recipe_yield");
            e.HasKey(y => y.Id);
            e.HasOne(y => y.Recipe).WithMany().HasForeignKey(y => y.RecipeId);
            e.HasIndex(y => new { y.RecipeId, y.Portions }).IsUnique();
        });

        model.Entity<HelloFreshYieldIngredientEntity>(e =>
        {
            e.ToTable("recipe_yield_ingredient");
            e.HasKey(x => new { x.YieldId, x.IngredientId });
            e.Property(x => x.Unit).HasMaxLength(128);
            e.HasOne(x => x.Yield).WithMany().HasForeignKey(x => x.YieldId);
            e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId);
        });

        model.Entity<HelloFreshStepEntity>(e =>
        {
            e.ToTable("recipe_step");
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Recipe).WithMany().HasForeignKey(s => s.RecipeId);
            e.HasIndex(s => new { s.RecipeId, s.Index }).IsUnique();
        });

        model.Entity<HelloFreshNutritionEntity>(e =>
        {
            e.ToTable("recipe_nutrition");
            e.HasKey(n => n.Id);
            e.Property(n => n.Name).HasMaxLength(256).IsRequired();
            e.Property(n => n.Unit).HasMaxLength(64);
            e.HasOne(n => n.Recipe).WithMany().HasForeignKey(n => n.RecipeId);
            e.HasIndex(n => new { n.RecipeId, n.Name }).IsUnique();
        });
    }

    /// <summary>Shared shape for the id/name/slug lookups.</summary>
    private static void Lookup<T>(ModelBuilder model, string table)
        where T : class
    {
        model.Entity<T>(e =>
        {
            e.ToTable(table);
            e.HasKey("Id");
            e.Property<string>("ExternalId").HasMaxLength(128).IsRequired();
            e.Property<string>("Name").HasMaxLength(256).IsRequired();
            e.Property<string>("Slug").HasMaxLength(256);
            e.HasIndex("ExternalId").IsUnique();
        });
    }
}
