namespace Mealplan.Infrastructure.Gousto.Persistence;

// The gousto schema models what Gousto actually publishes. Nothing here is
// shared with another source - hellofresh.ingredient is a separate universe by
// design, and cross-source reads go through the union views.

public class GoustoRecipeEntity
{
    public Guid Id { get; set; }

    /// <summary>The slug from the recipe URL. The detail endpoint's key.</summary>
    public required string Slug { get; set; }

    public string? GoustoUid { get; set; }

    public string? GoustoId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public double? RatingAverage { get; set; }

    public int? RatingCount { get; set; }

    public string? ImageUrl { get; set; }

    public Guid? CuisineId { get; set; }

    public GoustoCuisineEntity? Cuisine { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GoustoYieldEntity> Yields { get; set; } = [];

    public ICollection<GoustoStepEntity> Steps { get; set; } = [];

    public ICollection<GoustoRecipeAllergenEntity> Allergens { get; set; } = [];

    public ICollection<GoustoRecipeCategoryEntity> Categories { get; set; } = [];

    public ICollection<GoustoPantryItemEntity> PantryItems { get; set; } = [];

    public ICollection<GoustoNutritionEntity> Nutrition { get; set; } = [];
}

public class GoustoCuisineEntity
{
    public Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }
}

public class GoustoCategoryEntity
{
    public Guid Id { get; set; }

    public required string Uid { get; set; }

    public required string Title { get; set; }

    public string? Url { get; set; }
}

public class GoustoRecipeCategoryEntity
{
    public Guid RecipeId { get; set; }

    public GoustoRecipeEntity? Recipe { get; set; }

    public Guid CategoryId { get; set; }

    public GoustoCategoryEntity? Category { get; set; }
}

public class GoustoAllergenEntity
{
    public Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }
}

public class GoustoRecipeAllergenEntity
{
    public Guid RecipeId { get; set; }

    public GoustoRecipeEntity? Recipe { get; set; }

    public Guid AllergenId { get; set; }

    public GoustoAllergenEntity? Allergen { get; set; }
}

/// <summary>
/// A store-cupboard item the cook supplies. Gousto lists these separately from
/// what it ships, which is why they are not ingredients.
/// </summary>
public class GoustoPantryItemEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public GoustoRecipeEntity? Recipe { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }
}

public class GoustoIngredientEntity
{
    public Guid Id { get; set; }

    /// <summary>Gousto's own ingredient uuid. Joins from the portion SKU list.</summary>
    public required string GoustoUuid { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Gousto's display label, verbatim. Sometimes carries a weight in free text
    /// ("Intense chicken stock mix (5.5g)"), sometimes not ("Whole cucumber x0").
    /// Kept as-is so the caller sees what Gousto said rather than a guess.
    /// </summary>
    public string? Label { get; set; }
}

/// <summary>One offered portion count for a recipe.</summary>
public class GoustoYieldEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public GoustoRecipeEntity? Recipe { get; set; }

    public int Portions { get; set; }

    /// <summary>Only published for 2 and 4 portions; null otherwise.</summary>
    public int? PrepMinutes { get; set; }

    public bool IsOffered { get; set; }

    public ICollection<GoustoYieldIngredientEntity> Ingredients { get; set; } = [];
}

/// <summary>
/// An ingredient in one portion size. Amount and unit do not exist here: Gousto
/// ships boxes, not quantities. <see cref="InBox"/> is how many of that SKU go in
/// the box, which is not the same thing and is not presented as one.
/// </summary>
public class GoustoYieldIngredientEntity
{
    public Guid YieldId { get; set; }

    public GoustoYieldEntity? Yield { get; set; }

    public Guid IngredientId { get; set; }

    public GoustoIngredientEntity? Ingredient { get; set; }

    public string? SkuCode { get; set; }

    public int? InBox { get; set; }
}

public class GoustoStepEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public GoustoRecipeEntity? Recipe { get; set; }

    public int Order { get; set; }

    /// <summary>Gousto publishes instructions as HTML.</summary>
    public required string InstructionHtml { get; set; }

    /// <summary>Tags stripped, for callers that want plain text.</summary>
    public required string InstructionText { get; set; }
}

public enum NutritionBasis
{
    PerPortion = 1,
    PerHundredGrams = 2,
}

public class GoustoNutritionEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public GoustoRecipeEntity? Recipe { get; set; }

    public NutritionBasis Basis { get; set; }

    public double? EnergyKcal { get; set; }

    public double? EnergyKj { get; set; }

    public double? FatGrams { get; set; }

    public double? SaturatedFatGrams { get; set; }

    public double? CarbsGrams { get; set; }

    public double? SugarsGrams { get; set; }

    public double? FibreGrams { get; set; }

    public double? ProteinGrams { get; set; }

    public double? SaltGrams { get; set; }

    public double? NetWeightGrams { get; set; }
}
