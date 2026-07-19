namespace Mealplan.Infrastructure.HelloFresh.Persistence;

// The hellofresh schema models what HelloFresh publishes. Nothing is shared with
// the gousto schema; cross-source reads go through the union views.

public class HelloFreshRecipeEntity
{
    public Guid Id { get; set; }

    /// <summary>HelloFresh's own recipe id. Stable across renames.</summary>
    public required string ExternalId { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public string? Headline { get; set; }

    public string? Description { get; set; }

    /// <summary>1 to 3 as published. Not mapped to words - that would be invention.</summary>
    public int? Difficulty { get; set; }

    public int? PrepMinutes { get; set; }

    public int? TotalMinutes { get; set; }

    /// <summary>Grams per portion, as published.</summary>
    public double? ServingSizeGrams { get; set; }

    public double? AverageRating { get; set; }

    public int? RatingsCount { get; set; }

    public string? ImageUrl { get; set; }

    public string? WebsiteUrl { get; set; }

    public Guid? CategoryId { get; set; }

    public HelloFreshCategoryEntity? Category { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class HelloFreshCategoryEntity
{
    public Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? Slug { get; set; }
}

public class HelloFreshCuisineEntity
{
    public Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? Slug { get; set; }
}

public class HelloFreshRecipeCuisineEntity
{
    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public Guid CuisineId { get; set; }

    public HelloFreshCuisineEntity? Cuisine { get; set; }
}

public class HelloFreshTagEntity
{
    public Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? Slug { get; set; }
}

public class HelloFreshRecipeTagEntity
{
    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public Guid TagId { get; set; }

    public HelloFreshTagEntity? Tag { get; set; }
}

public class HelloFreshAllergenEntity
{
    public Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? Slug { get; set; }
}

public class HelloFreshRecipeAllergenEntity
{
    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public Guid AllergenId { get; set; }

    public HelloFreshAllergenEntity? Allergen { get; set; }

    /// <summary>
    /// "May contain traces of" rather than "contains". Kept apart because
    /// collapsing the two would misreport what a recipe actually holds.
    /// </summary>
    public bool TracesOf { get; set; }
}

public class HelloFreshUtensilEntity
{
    public Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public required string Name { get; set; }
}

public class HelloFreshRecipeUtensilEntity
{
    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public Guid UtensilId { get; set; }

    public HelloFreshUtensilEntity? Utensil { get; set; }
}

public class HelloFreshIngredientEntity
{
    public Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? Slug { get; set; }

    /// <summary>HelloFresh groups ingredients into families, e.g. "garlic".</summary>
    public string? Family { get; set; }

    public string? ImageUrl { get; set; }
}

public class HelloFreshYieldEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public int Portions { get; set; }
}

/// <summary>
/// A measured ingredient for one portion count. HelloFresh publishes a real
/// amount and unit here, which is what makes its recipes shoppable.
/// </summary>
public class HelloFreshYieldIngredientEntity
{
    public Guid YieldId { get; set; }

    public HelloFreshYieldEntity? Yield { get; set; }

    public Guid IngredientId { get; set; }

    public HelloFreshIngredientEntity? Ingredient { get; set; }

    public double? Amount { get; set; }

    public string? Unit { get; set; }
}

public class HelloFreshStepEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public int Index { get; set; }

    public required string Instructions { get; set; }
}

/// <summary>
/// Nutrition as published: a named, typed list rather than fixed columns, since
/// HelloFresh varies which nutrients a recipe carries.
/// </summary>
public class HelloFreshNutritionEntity
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public HelloFreshRecipeEntity? Recipe { get; set; }

    public required string Name { get; set; }

    public double? Amount { get; set; }

    public string? Unit { get; set; }
}
