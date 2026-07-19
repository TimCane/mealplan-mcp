using Hangfire;
using Mealplan.Infrastructure;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Scraper;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMealplanInfrastructure(builder.Configuration);
builder.Services.AddScraperJobs(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// The scraper owns the scrape schema, so it is the one host that migrates it.
// The MCP server only reads and must not race it at startup.
if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ScrapeDbContext>().Database.MigrateAsync();
}

// Local only: the dashboard has no authentication, and the container is not
// meant to be reachable from outside the compose network.
app.UseHangfireDashboard("/jobs");
app.MapHealthChecks("/health");

app.Services.ScheduleSourceCrawls();

await app.RunAsync();
