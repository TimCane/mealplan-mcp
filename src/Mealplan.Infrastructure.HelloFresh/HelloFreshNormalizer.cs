using System.Text.Json;
using System.Xml;
using Mealplan.Domain.Scraping;
using Mealplan.Domain.Sources;
using Mealplan.Infrastructure.HelloFresh.Api;
using Mealplan.Infrastructure.HelloFresh.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mealplan.Infrastructure.HelloFresh;

/// <summary>
/// Maps a stored HelloFresh recipe into the hellofresh schema. The crawler
/// stores one document per recipe, so there is nothing to unpack here.
/// </summary>
public class HelloFreshNormalizer(HelloFreshDbContext db, TimeProvider clock) : ISourceNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Source => HelloFreshSchema.SourceSlug;

    public IReadOnlySet<DocumentType> Handles { get; } = new HashSet<DocumentType>
    {
        DocumentType.Recipe,
    };

    public async Task NormalizeAsync(ScrapeDocument document, CancellationToken ct = default)
    {
        var source = JsonSerializer.Deserialize<HelloFreshRecipe>(document.Payload, JsonOptions)
            ?? throw new InvalidOperationException(
                $"HelloFresh payload for '{document.SourceKey}' could not be read.");

        if (string.IsNullOrWhiteSpace(source.Id))
        {
            throw new InvalidOperationException(
                $"HelloFresh payload for '{document.SourceKey}' has no id.");
        }

        var recipe = await LoadOrCreateAsync(source.Id, ct);

        recipe.Slug = source.Slug ?? source.Id;
        recipe.Name = source.Name?.Trim() ?? source.Id;
        recipe.Headline = source.Headline;
        recipe.Description = source.Description;
        recipe.Difficulty = source.Difficulty;
        recipe.PrepMinutes = Minutes(source.PrepTime);
        recipe.TotalMinutes = Minutes(source.TotalTime);
        recipe.ServingSizeGrams = source.ServingSize;
        recipe.AverageRating = source.AverageRating;
        recipe.RatingsCount = source.RatingsCount;
        recipe.ImageUrl = source.ImageLink;
        recipe.WebsiteUrl = source.WebsiteUrl;
        recipe.UpdatedAt = clock.GetUtcNow();
        recipe.CategoryId = await CategoryIdAsync(source.Category, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The recipe row must exist before its children can reference it.
        await db.SaveChangesAsync(ct);
        await ClearChildrenAsync(recipe.Id, ct);

        await AddYieldsAsync(recipe, source, ct);
        AddSteps(recipe, source);
        AddNutrition(recipe, source);
        await AddCuisinesAsync(recipe, source, ct);
        await AddTagsAsync(recipe, source, ct);
        await AddAllergensAsync(recipe, source, ct);
        await AddUtensilsAsync(recipe, source, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<HelloFreshRecipeEntity> LoadOrCreateAsync(string externalId, CancellationToken ct)
    {
        var existing = await db.Recipes.FirstOrDefaultAsync(r => r.ExternalId == externalId, ct);

        if (existing is not null)
        {
            return existing;
        }

        var created = new HelloFreshRecipeEntity
        {
            Id = Guid.CreateVersion7(),
            ExternalId = externalId,
            Slug = externalId,
            Name = externalId,
        };

        db.Recipes.Add(created);
        return created;
    }

    private async Task ClearChildrenAsync(Guid recipeId, CancellationToken ct)
    {
        await db.Set<HelloFreshYieldIngredientEntity>()
            .Where(x => x.Yield!.RecipeId == recipeId)
            .ExecuteDeleteAsync(ct);

        await db.Yields.Where(y => y.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Steps.Where(s => s.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Nutrition.Where(n => n.RecipeId == recipeId).ExecuteDeleteAsync(ct);

        await db.Set<HelloFreshRecipeCuisineEntity>()
            .Where(x => x.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Set<HelloFreshRecipeTagEntity>()
            .Where(x => x.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Set<HelloFreshRecipeAllergenEntity>()
            .Where(x => x.RecipeId == recipeId).ExecuteDeleteAsync(ct);
        await db.Set<HelloFreshRecipeUtensilEntity>()
            .Where(x => x.RecipeId == recipeId).ExecuteDeleteAsync(ct);
    }

    private async Task AddYieldsAsync(
        HelloFreshRecipeEntity recipe,
        HelloFreshRecipe source,
        CancellationToken ct)
    {
        var ingredients = await IngredientMapAsync(source, ct);

        foreach (var yield in (source.Yields ?? []).Where(y => y.Yields is not null))
        {
            var entity = new HelloFreshYieldEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Portions = yield.Yields!.Value,
            };

            // Added through the DbSet rather than a navigation: children carry
            // client-set keys, and a tracked parent's collection would have EF
            // treat them as existing rows and update nothing.
            db.Yields.Add(entity);

            var seen = new HashSet<Guid>();

            foreach (var line in yield.Ingredients ?? [])
            {
                if (line.Id is null || !ingredients.TryGetValue(line.Id, out var ingredient))
                {
                    continue;
                }

                if (!seen.Add(ingredient.Id))
                {
                    continue;
                }

                db.Add(new HelloFreshYieldIngredientEntity
                {
                    YieldId = entity.Id,
                    IngredientId = ingredient.Id,
                    Amount = line.Amount,
                    Unit = line.Unit,
                });
            }
        }
    }

    private async Task<Dictionary<string, HelloFreshIngredientEntity>> IngredientMapAsync(
        HelloFreshRecipe source,
        CancellationToken ct)
    {
        var incoming = (source.Ingredients ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .GroupBy(i => i.Id!)
            .Select(g => g.First())
            .ToList();

        var ids = incoming.Select(i => i.Id!).ToList();

        var existing = await db.Ingredients
            .Where(i => ids.Contains(i.ExternalId))
            .ToDictionaryAsync(i => i.ExternalId, ct);

        foreach (var item in incoming)
        {
            if (existing.TryGetValue(item.Id!, out var entity))
            {
                entity.Name = item.Name?.Trim() ?? entity.Name;
                entity.Family = item.Family?.Name;
                continue;
            }

            var created = new HelloFreshIngredientEntity
            {
                Id = Guid.CreateVersion7(),
                ExternalId = item.Id!,
                Name = item.Name?.Trim() ?? item.Id!,
                Slug = item.Slug,
                Family = item.Family?.Name,
                ImageUrl = item.ImageLink,
            };

            db.Ingredients.Add(created);
            existing[created.ExternalId] = created;
        }

        return existing;
    }

    private void AddSteps(HelloFreshRecipeEntity recipe, HelloFreshRecipe source)
    {
        foreach (var step in (source.Steps ?? []).Where(s => s.Instructions is not null))
        {
            db.Steps.Add(new HelloFreshStepEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Index = step.Index,
                Instructions = step.Instructions!,
            });
        }
    }

    private void AddNutrition(HelloFreshRecipeEntity recipe, HelloFreshRecipe source)
    {
        // Names are the unique key per recipe, and HelloFresh has been seen to
        // repeat one; the first wins rather than the insert failing.
        var nutrients = (source.Nutrition ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());

        foreach (var nutrient in nutrients)
        {
            db.Nutrition.Add(new HelloFreshNutritionEntity
            {
                Id = Guid.CreateVersion7(),
                RecipeId = recipe.Id,
                Name = nutrient.Name!,
                Amount = nutrient.Amount,
                Unit = nutrient.Unit,
            });
        }
    }

    private async Task AddCuisinesAsync(
        HelloFreshRecipeEntity recipe,
        HelloFreshRecipe source,
        CancellationToken ct)
    {
        foreach (var taxon in Distinct(source.Cuisines))
        {
            var cuisine = await db.Cuisines.FirstOrDefaultAsync(c => c.ExternalId == taxon.Id, ct);

            if (cuisine is null)
            {
                cuisine = new HelloFreshCuisineEntity
                {
                    Id = Guid.CreateVersion7(),
                    ExternalId = taxon.Id!,
                    Name = taxon.Name ?? taxon.Id!,
                    Slug = taxon.Slug,
                };

                db.Cuisines.Add(cuisine);
            }

            db.Add(new HelloFreshRecipeCuisineEntity
            {
                RecipeId = recipe.Id,
                CuisineId = cuisine.Id,
            });
        }
    }

    private async Task AddTagsAsync(
        HelloFreshRecipeEntity recipe,
        HelloFreshRecipe source,
        CancellationToken ct)
    {
        foreach (var taxon in Distinct(source.Tags))
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.ExternalId == taxon.Id, ct);

            if (tag is null)
            {
                tag = new HelloFreshTagEntity
                {
                    Id = Guid.CreateVersion7(),
                    ExternalId = taxon.Id!,
                    Name = taxon.Name ?? taxon.Id!,
                    Slug = taxon.Slug,
                };

                db.Tags.Add(tag);
            }

            db.Add(new HelloFreshRecipeTagEntity { RecipeId = recipe.Id, TagId = tag.Id });
        }
    }

    private async Task AddAllergensAsync(
        HelloFreshRecipeEntity recipe,
        HelloFreshRecipe source,
        CancellationToken ct)
    {
        // HelloFresh repeats allergens across ingredients, and lists "contains"
        // and "may contain traces of" as separate entries for the same id. The
        // stricter one wins: understating an allergen is the dangerous direction.
        var incoming = (source.Allergens ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .GroupBy(a => a.Id!)
            .Select(g => new
            {
                Allergen = g.First(),
                TracesOf = g.All(a => a.TracesOf),
            });

        foreach (var item in incoming)
        {
            var allergen = await db.Allergens
                .FirstOrDefaultAsync(a => a.ExternalId == item.Allergen.Id, ct);

            if (allergen is null)
            {
                allergen = new HelloFreshAllergenEntity
                {
                    Id = Guid.CreateVersion7(),
                    ExternalId = item.Allergen.Id!,
                    Name = item.Allergen.Name ?? item.Allergen.Id!,
                    Slug = item.Allergen.Slug,
                };

                db.Allergens.Add(allergen);
            }

            db.Add(new HelloFreshRecipeAllergenEntity
            {
                RecipeId = recipe.Id,
                AllergenId = allergen.Id,
                TracesOf = item.TracesOf,
            });
        }
    }

    private async Task AddUtensilsAsync(
        HelloFreshRecipeEntity recipe,
        HelloFreshRecipe source,
        CancellationToken ct)
    {
        foreach (var taxon in Distinct(source.Utensils))
        {
            var utensil = await db.Utensils.FirstOrDefaultAsync(u => u.ExternalId == taxon.Id, ct);

            if (utensil is null)
            {
                utensil = new HelloFreshUtensilEntity
                {
                    Id = Guid.CreateVersion7(),
                    ExternalId = taxon.Id!,
                    Name = taxon.Name ?? taxon.Id!,
                };

                db.Utensils.Add(utensil);
            }

            db.Add(new HelloFreshRecipeUtensilEntity
            {
                RecipeId = recipe.Id,
                UtensilId = utensil.Id,
            });
        }
    }

    private async Task<Guid?> CategoryIdAsync(HelloFreshTaxon? taxon, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taxon?.Id))
        {
            return null;
        }

        var category = await db.Categories.FirstOrDefaultAsync(c => c.ExternalId == taxon.Id, ct);

        if (category is null)
        {
            category = new HelloFreshCategoryEntity
            {
                Id = Guid.CreateVersion7(),
                ExternalId = taxon.Id,
                Name = taxon.Name ?? taxon.Id,
                Slug = taxon.Slug,
            };

            db.Categories.Add(category);
        }

        return category.Id;
    }

    private static IEnumerable<HelloFreshTaxon> Distinct(IReadOnlyList<HelloFreshTaxon>? taxa) =>
        (taxa ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .GroupBy(t => t.Id!)
            .Select(g => g.First());

    /// <summary>
    /// HelloFresh publishes durations as ISO 8601, e.g. PT20M. Parsed rather
    /// than pattern-matched so PT1H15M is not silently read as 15 minutes.
    /// </summary>
    internal static int? Minutes(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        try
        {
            return (int)XmlConvert.ToTimeSpan(duration).TotalMinutes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
