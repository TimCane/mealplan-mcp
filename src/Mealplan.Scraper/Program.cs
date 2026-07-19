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

// The scraper writes, so it is the one host that migrates. The MCP server only
// reads and must not race it at startup.
if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAllAsync(app.Services);
}

// Local only: the dashboard has no authentication, and the container is not
// meant to be reachable from outside the compose network.
app.UseHangfireDashboard("/jobs");
app.MapHealthChecks("/health");

app.Services.ScheduleSourceCrawls();

await app.RunAsync();
