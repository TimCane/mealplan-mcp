using Hangfire;
using Mealplan.Infrastructure;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Scraper;

var oneShot = OneShotCommand.Parse(args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMealplanInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// Hangfire is only needed for the scheduled server, not a one-shot run.
if (oneShot is null)
{
    builder.Services.AddScraperJobs(builder.Configuration);
}

var app = builder.Build();

// The scraper writes, so it is the one host that migrates. The MCP server only
// reads and must not race it at startup.
if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAllAsync(app.Services);
}

if (oneShot is not null)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mealplan.Scraper");

    return await oneShot.RunAsync(app.Services, logger);
}

// Local only: the dashboard has no authentication, and the container is not
// meant to be reachable from outside the compose network.
app.UseHangfireDashboard("/jobs");
app.MapHealthChecks("/health");

await app.Services.ScheduleSourceCrawlsAsync();

await app.RunAsync();

return 0;
