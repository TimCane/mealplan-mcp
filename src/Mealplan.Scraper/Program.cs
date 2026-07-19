using Hangfire;
using Mealplan.Infrastructure;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Scraper;
using Microsoft.AspNetCore.HttpOverrides;

var oneShot = OneShotCommand.Parse(args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMealplanInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// The proxy terminates TLS and forwards plain HTTP, so without these the app
// takes every request for http:// on an internal host and any absolute URL it
// builds - the dashboard's redirects included - points somewhere unreachable.
// The allowlist is cleared because the proxy's address on the Docker network is
// not stable, and nothing off the compose network can reach this port to
// spoof the headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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

// First in the pipeline: everything downstream reads the corrected scheme and
// host off the request.
app.UseForwardedHeaders();

app.UseHangfireDashboard("/jobs", new DashboardOptions
{
    Authorization = [app.Services.GetRequiredService<BasicAuthDashboardFilter>()],
});
app.MapHealthChecks("/health");

await app.Services.ScheduleSourceCrawlsAsync();

await app.RunAsync();

return 0;
