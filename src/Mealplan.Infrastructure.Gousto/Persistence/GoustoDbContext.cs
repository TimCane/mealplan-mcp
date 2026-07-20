using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.Gousto.Persistence;

/// <summary>
/// Owns the <c>gousto</c> schema and its own migration history, so migrations
/// here never serialise against another source's.
/// </summary>
public class GoustoDbContext(DbContextOptions<GoustoDbContext> options) : DbContext(options)
{
    public const string SchemaName = "gousto";

    public DbSet<GoustoRecipeEntity> Recipes => Set<GoustoRecipeEntity>();

    public DbSet<GoustoCuisineEntity> Cuisines => Set<GoustoCuisineEntity>();

    public DbSet<GoustoCategoryEntity> Categories => Set<GoustoCategoryEntity>();

    public DbSet<GoustoAllergenEntity> Allergens => Set<GoustoAllergenEntity>();

    public DbSet<GoustoIngredientEntity> Ingredients => Set<GoustoIngredientEntity>();

    public DbSet<GoustoYieldEntity> Yields => Set<GoustoYieldEntity>();

    public DbSet<GoustoStepEntity> Steps => Set<GoustoStepEntity>();

    public DbSet<GoustoNutritionEntity> Nutrition => Set<GoustoNutritionEntity>();

    public DbSet<GoustoPantryItemEntity> PantryItems => Set<GoustoPantryItemEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);

        model.Entity<GoustoRecipeEntity>(e =>
        {
            e.ToTable("recipe");
            e.HasKey(r => r.Id);
            e.Property(r => r.Slug).HasMaxLength(512).IsRequired();
            e.Property(r => r.Title).HasMaxLength(512).IsRequired();
            e.Property(r => r.GoustoUid).HasMaxLength(128);
            e.Property(r => r.GoustoId).HasMaxLength(64);
            e.Property(r => r.WebsiteUrl).HasMaxLength(1024);
            e.HasIndex(r => r.Slug).IsUnique();
            e.HasOne(r => r.Cuisine).WithMany().HasForeignKey(r => r.CuisineId);
        });

        model.Entity<GoustoCuisineEntity>(e =>
        {
            e.ToTable("cuisine");
            e.HasKey(c => c.Id);
            e.Property(c => c.Slug).HasMaxLength(128).IsRequired();
            e.Property(c => c.Title).HasMaxLength(256).IsRequired();
            e.HasIndex(c => c.Slug).IsUnique();
        });

        model.Entity<GoustoCategoryEntity>(e =>
        {
            e.ToTable("category");
            e.HasKey(c => c.Id);
            e.Property(c => c.Uid).HasMaxLength(128).IsRequired();
            e.Property(c => c.Title).HasMaxLength(256).IsRequired();
            e.Property(c => c.Url).HasMaxLength(512);
            e.HasIndex(c => c.Uid).IsUnique();
        });

        model.Entity<GoustoRecipeCategoryEntity>(e =>
        {
            e.ToTable("recipe_category");
            e.HasKey(rc => new { rc.RecipeId, rc.CategoryId });
            e.HasOne(rc => rc.Recipe).WithMany(r => r.Categories).HasForeignKey(rc => rc.RecipeId);
            e.HasOne(rc => rc.Category).WithMany().HasForeignKey(rc => rc.CategoryId);
        });

        model.Entity<GoustoAllergenEntity>(e =>
        {
            e.ToTable("allergen");
            e.HasKey(a => a.Id);
            e.Property(a => a.Slug).HasMaxLength(128).IsRequired();
            e.Property(a => a.Title).HasMaxLength(256).IsRequired();
            e.HasIndex(a => a.Slug).IsUnique();
        });

        model.Entity<GoustoRecipeAllergenEntity>(e =>
        {
            e.ToTable("recipe_allergen");
            e.HasKey(ra => new { ra.RecipeId, ra.AllergenId });
            e.HasOne(ra => ra.Recipe).WithMany(r => r.Allergens).HasForeignKey(ra => ra.RecipeId);
            e.HasOne(ra => ra.Allergen).WithMany().HasForeignKey(ra => ra.AllergenId);
        });

        model.Entity<GoustoPantryItemEntity>(e =>
        {
            e.ToTable("pantry_item");
            e.HasKey(p => p.Id);
            e.Property(p => p.Slug).HasMaxLength(128).IsRequired();
            e.Property(p => p.Title).HasMaxLength(256).IsRequired();
            e.HasOne(p => p.Recipe).WithMany(r => r.PantryItems).HasForeignKey(p => p.RecipeId);
            e.HasIndex(p => new { p.RecipeId, p.Slug }).IsUnique();
        });

        model.Entity<GoustoIngredientEntity>(e =>
        {
            e.ToTable("ingredient");
            e.HasKey(i => i.Id);
            e.Property(i => i.GoustoUuid).HasMaxLength(128).IsRequired();
            e.Property(i => i.Name).HasMaxLength(512).IsRequired();
            e.Property(i => i.Label).HasMaxLength(512);
            e.HasIndex(i => i.GoustoUuid).IsUnique();
        });

        model.Entity<GoustoYieldEntity>(e =>
        {
            e.ToTable("recipe_yield");
            e.HasKey(y => y.Id);
            e.HasOne(y => y.Recipe).WithMany(r => r.Yields).HasForeignKey(y => y.RecipeId);
            e.HasIndex(y => new { y.RecipeId, y.Portions }).IsUnique();
        });

        model.Entity<GoustoYieldIngredientEntity>(e =>
        {
            e.ToTable("recipe_yield_ingredient");
            e.HasKey(yi => new { yi.YieldId, yi.IngredientId });
            e.Property(yi => yi.SkuCode).HasMaxLength(128);
            e.HasOne(yi => yi.Yield).WithMany(y => y.Ingredients).HasForeignKey(yi => yi.YieldId);
            e.HasOne(yi => yi.Ingredient).WithMany().HasForeignKey(yi => yi.IngredientId);
        });

        model.Entity<GoustoStepEntity>(e =>
        {
            e.ToTable("recipe_step");
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Recipe).WithMany(r => r.Steps).HasForeignKey(s => s.RecipeId);
            e.HasIndex(s => new { s.RecipeId, s.Order }).IsUnique();
        });

        model.Entity<GoustoNutritionEntity>(e =>
        {
            e.ToTable("recipe_nutrition");
            e.HasKey(n => n.Id);
            e.HasOne(n => n.Recipe).WithMany(r => r.Nutrition).HasForeignKey(n => n.RecipeId);
            e.HasIndex(n => new { n.RecipeId, n.Basis }).IsUnique();
        });
    }
}
