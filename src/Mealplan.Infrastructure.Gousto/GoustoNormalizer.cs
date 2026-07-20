using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.Gousto.Api;
using Mealplan.Infrastructure.Gousto.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.Gousto;

/// <summary>
/// Maps a stored Gousto payload into the gousto schema. Only recipe documents
/// are handled: list pages exist to discover slugs and detect change.
/// </summary>
public partial class GoustoNormalizer(GoustoDbContext db, TimeProvider clock) : ISourceNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Source => GoustoSchema.SourceSlug;

    public IReadOnlySet<DocumentType> Handles { get; } = new HashSet<DocumentType>
    {
        DocumentType.Recipe,
    };

    public async Task NormalizeAsync(ScrapeDocument document, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<GoustoDetailResponse>(document.Payload, JsonOptions);
        var source = payload?.Data?.Entry
            ?? throw new InvalidOperationException(
                $"Gousto payload for '{document.SourceKey}' has no data.entry.");

        var slug = document.SourceKey;
        var recipe = await LoadOrCreateAsync(slug, ct);

        recipe.Title = source.Title?.Trim() ?? slug;
        recipe.Description = source.Description;
        recipe.GoustoUid = source.GoustoUid;
        recipe.GoustoId = source.GoustoId;
        recipe.RatingAverage = source.Rating?.Average;
        recipe.RatingCount = source.Rating?.Count;
        recipe.ImageUrl = LargestImage(source.Media);
        recipe.WebsiteUrl = source.Seo?.Canonical;
        recipe.UpdatedAt = clock.GetUtcNow();
        recipe.CuisineId = await CuisineIdAsync(source.Cuisine, ct);

        // Children are replaced wholesale. A recipe is small and upstream edits
        // reorder as often as they add, so diffing would cost more than it saves.
        //
        // The deletes are flushed before the inserts because the replacements
        // reuse the same unique keys - (recipe, portions), (recipe, order) and
        // the join-table primary keys. In one SaveChanges, EF is free to order
        // an insert ahead of the delete it replaces, and Postgres rejects it.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The recipe row must exist before its children can reference it.
        await db.SaveChangesAsync(ct);
        await ClearChildrenAsync(recipe.Id, ct);

        AddYields(recipe, source, await IngredientMapAsync(source, ct));
        AddSteps(recipe, source);
        AddNutrition(recipe, source);
        await AddAllergensAsync(recipe, source, ct);
        await AddCategoriesAsync(recipe, source, ct);
        AddPantryItems(recipe, source);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Loads the recipe row only. Children are deleted set-based rather than
    /// tracked, so there is nothing stale to load them into.
    /// </summary>
    private async Task<GoustoRecipeEntity> LoadOrCreateAsync(string slug, CancellationToken ct)
    {
        var existing = await db.Recipes.FirstOrDefaultAsync(r => r.Slug == slug, ct);

        if (existing is not null)
        {
            return existing;
        }

        var created = new GoustoRecipeEntity { Id = Guid.CreateVersion7(), Slug = slug, Title = slug };
        db.Recipes.Add(created);
        return created;
    }

    /// <summary>
    /// Deletes this recipe's children with set-based statements. Loading them
    /// only to mark each one Deleted made the outcome depend on what the change
    /// tracker happened to hold, which is not a property worth having.
    /// </summary>
    private async Task ClearChildrenAsync(Guid recipeId, CancellationToken ct)
    {
        await db.Set<GoustoYieldIngredientEntity>()
            .Where(yi => yi.Yield!.RecipeId == recipeId)
            .ExecuteDeleteAsync(ct);

        await db.Yields.Where(y => y.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Steps.Where(s => s.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Nutrition.Where(n => n.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.PantryItems.Where(p => p.RecipeId == recipeId).ExecuteDeleteAsync(ct);

        await db.Set<GoustoRecipeAllergenEntity>()
            .Where(ra => ra.RecipeId == recipeId)
            .ExecuteDeleteAsync(ct);

        await db.Set<GoustoRecipeCategoryEntity>()
            .Where(rc => rc.RecipeId == recipeId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Gousto's ingredient catalogue, keyed by the uuid that portion SKUs join on.
    /// Ingredients are shared across recipes, so they are upserted, not replaced.
    /// </summary>
    private async Task<Dictionary<string, GoustoIngredientEntity>> IngredientMapAsync(
        GoustoRecipe source,
        CancellationToken ct)
    {
        var incoming = (source.Ingredients ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.GoustoUuid))
            .GroupBy(i => i.GoustoUuid!)
            .Select(g => g.First())
            .ToList();

        var uuids = incoming.Select(i => i.GoustoUuid!).ToList();

        var existing = await db.Ingredients
            .Where(i => uuids.Contains(i.GoustoUuid))
            .ToDictionaryAsync(i => i.GoustoUuid, ct);

        foreach (var item in incoming)
        {
            if (existing.TryGetValue(item.GoustoUuid!, out var entity))
            {
                entity.Name = item.Name?.Trim() ?? entity.Name;
                entity.Label = item.Label;
                continue;
            }

            var created = new GoustoIngredientEntity
            {
                Id = Guid.CreateVersion7(),
                GoustoUuid = item.GoustoUuid!,
                Name = item.Name?.Trim() ?? item.Label?.Trim() ?? item.GoustoUuid!,
                Label = item.Label,
            };

            db.Ingredients.Add(created);
            existing[created.GoustoUuid] = created;
        }

        return existing;
    }

    private void AddYields(
        GoustoRecipeEntity recipe,
        GoustoRecipe source,
        Dictionary<string, GoustoIngredientEntity> ingredients)
    {
        foreach (var portion in source.PortionSizes ?? [])
        {
            var entity = new GoustoYieldEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Portions = portion.Portions,
                IsOffered = portion.IsOffered,

                // Gousto publishes prep time for 2 and 4 portions only. Every
                // other portion count genuinely has none - it is not a default.
                PrepMinutes = portion.Portions switch
                {
                    2 => source.PrepTimes?.For2,
                    4 => source.PrepTimes?.For4,
                    _ => null,
                },
            };

            var seen = new HashSet<Guid>();

            foreach (var sku in portion.IngredientSkus ?? [])
            {
                if (sku.Id is null || !ingredients.TryGetValue(sku.Id, out var ingredient))
                {
                    continue;
                }

                // A portion can list the same ingredient twice; the key is
                // (yield, ingredient), so fold duplicates into one row.
                if (!seen.Add(ingredient.Id))
                {
                    continue;
                }

                entity.Ingredients.Add(new GoustoYieldIngredientEntity
                {
                    YieldId = entity.Id,
                    IngredientId = ingredient.Id,
                    SkuCode = sku.Code,
                    InBox = sku.Quantities?.InBox,
                });
            }

            // Added explicitly rather than through recipe.Yields alone: the
            // dependent rows carry a client-set YieldId, and relying on graph
            // fixup to order the parent insert first is how this broke before.
            db.Yields.Add(entity);
        }
    }

    private void AddSteps(GoustoRecipeEntity recipe, GoustoRecipe source)
    {
        foreach (var step in (source.CookingInstructions ?? []).Where(s => s.Instruction is not null))
        {
            db.Steps.Add(new GoustoStepEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Order = step.Order,
                InstructionHtml = step.Instruction!,
                InstructionText = StripHtml(step.Instruction!),
            });
        }
    }

    private void AddNutrition(GoustoRecipeEntity recipe, GoustoRecipe source)
    {
        Add(NutritionBasis.PerPortion, source.Nutrition?.PerPortion);
        Add(NutritionBasis.PerHundredGrams, source.Nutrition?.PerHundredGrams);

        void Add(NutritionBasis basis, GoustoNutrition? values)
        {
            if (values is null)
            {
                return;
            }

            db.Nutrition.Add(new GoustoNutritionEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Basis = basis,
                EnergyKcal = values.EnergyKcal,
                EnergyKj = values.EnergyKj,

                // Gousto publishes milligrams; grams is what a recipe reads in.
                FatGrams = MgToG(values.FatMg),
                SaturatedFatGrams = MgToG(values.SaturatedFatMg),
                CarbsGrams = MgToG(values.CarbsMg),
                SugarsGrams = MgToG(values.SugarsMg),
                FibreGrams = MgToG(values.FibreMg),
                ProteinGrams = MgToG(values.ProteinMg),
                SaltGrams = MgToG(values.SaltMg),
                NetWeightGrams = MgToG(values.NetWeightMg),
            });
        }
    }

    private async Task AddAllergensAsync(
        GoustoRecipeEntity recipe,
        GoustoRecipe source,
        CancellationToken ct)
    {
        // Recipe-level allergens are the published list. Ingredient-level ones
        // are a subset in practice, but union them so a recipe cannot understate
        // what it contains.
        var taxa = (source.Allergens ?? [])
            .Concat((source.Ingredients ?? []).SelectMany(i => i.Allergens?.Allergen ?? []))
            .Where(a => !string.IsNullOrWhiteSpace(a.Slug))
            .GroupBy(a => a.Slug!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var taxon in taxa)
        {
            var allergen = await db.Allergens.FirstOrDefaultAsync(a => a.Slug == taxon.Slug, ct);

            if (allergen is null)
            {
                allergen = new GoustoAllergenEntity
                {
                    Id = Guid.CreateVersion7(),
                    Slug = taxon.Slug!,
                    Title = taxon.Title ?? taxon.Slug!,
                };

                db.Allergens.Add(allergen);
            }

            db.Add(new GoustoRecipeAllergenEntity
            {
                RecipeId = recipe.Id,
                AllergenId = allergen.Id,
            });
        }
    }

    private async Task AddCategoriesAsync(
        GoustoRecipeEntity recipe,
        GoustoRecipe source,
        CancellationToken ct)
    {
        var incoming = (source.Categories ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Uid))
            .GroupBy(c => c.Uid!)
            .Select(g => g.First());

        foreach (var item in incoming)
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Uid == item.Uid, ct);

            if (category is null)
            {
                category = new GoustoCategoryEntity
                {
                    Id = Guid.CreateVersion7(),
                    Uid = item.Uid!,
                    Title = item.Title ?? item.Uid!,
                    Url = item.Url,
                };

                db.Categories.Add(category);
            }

            db.Add(new GoustoRecipeCategoryEntity
            {
                RecipeId = recipe.Id,
                CategoryId = category.Id,
            });
        }
    }

    private void AddPantryItems(GoustoRecipeEntity recipe, GoustoRecipe source)
    {
        var incoming = (source.Basics ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b.Slug))
            .GroupBy(b => b.Slug!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());

        foreach (var item in incoming)
        {
            db.PantryItems.Add(new GoustoPantryItemEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Slug = item.Slug!,
                Title = item.Title ?? item.Slug!,
            });
        }
    }

    private async Task<Guid?> CuisineIdAsync(GoustoTaxon? taxon, CancellationToken ct)
    {
        if (taxon?.Slug is null)
        {
            return null;
        }

        var cuisine = await db.Cuisines.FirstOrDefaultAsync(c => c.Slug == taxon.Slug, ct);

        if (cuisine is null)
        {
            cuisine = new GoustoCuisineEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = taxon.Slug,
                Title = taxon.Title ?? taxon.Slug,
            };

            db.Cuisines.Add(cuisine);
        }

        return cuisine.Id;
    }

    private static double? MgToG(double? milligrams) =>
        milligrams is null ? null : Math.Round(milligrams.Value / 1000d, 3);

    private static string? LargestImage(GoustoMedia? media) =>
        media?.Images?.OrderByDescending(i => i.Width ?? 0).FirstOrDefault()?.Image;

    internal static string StripHtml(string html)
    {
        // Gousto separates steps with block tags and no whitespace, so those
        // become a space before tags are dropped or words would run together.
        var spaced = BlockTag().Replace(html, " ");
        var text = AnyTag().Replace(spaced, string.Empty);

        return WhitespaceRun().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex("</(p|div|li|h[1-6])>|<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
