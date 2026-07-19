namespace Mealplan.Scraper;

/// <summary>
/// Credentials for the Hangfire dashboard. Both values are required whenever the
/// scheduled server runs; a one-shot command never registers them.
/// </summary>
public sealed class JobsDashboardOptions
{
    public const string SectionName = "JobsDashboard";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
