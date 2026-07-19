using Hangfire.Dashboard;
using Microsoft.Extensions.Options;

namespace Mealplan.Scraper;

/// <summary>
/// Gates the Hangfire dashboard on HTTP basic auth. The dashboard is reachable
/// on a public domain, and Hangfire ships no authentication of its own.
/// </summary>
public sealed class BasicAuthDashboardFilter(IOptionsMonitor<JobsDashboardOptions> options)
    : IDashboardAuthorizationFilter
{
    private const string Challenge = "Basic realm=\"mealplan-scraper\", charset=\"UTF-8\"";

    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();

        if (BasicCredentials.Match(http.Request.Headers.Authorization, options.CurrentValue))
        {
            return true;
        }

        // Hangfire answers 401 on a false return but sets no challenge header, so
        // a browser would show its own error page instead of a login prompt.
        http.Response.Headers.WWWAuthenticate = Challenge;
        return false;
    }
}
