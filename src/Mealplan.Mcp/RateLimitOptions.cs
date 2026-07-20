using System.ComponentModel.DataAnnotations;

namespace Mealplan.Mcp;

/// <summary>
/// Ceiling for the per-address fixed window. Set generously: no real agent
/// should ever see a 429, but a misbehaving loop cannot hammer the database.
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    [Range(1, 100_000)]
    public int PermitsPerMinute { get; set; } = 120;
}
