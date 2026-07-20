using Mealplan.Infrastructure;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Mcp.Tools;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMealplanInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// The proxy terminates TLS and forwards plain HTTP, so without these the app
// takes every request for http:// on an internal host and any absolute URL it
// builds points somewhere unreachable. OAuth metadata is the case that breaks
// outright: a resource URI advertised as http:// is rejected by MCP clients.
// The allowlist is cleared because the proxy's address on the Docker network is
// not stable, and nothing else can reach this container to spoof the headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "mealplan",
            Version = "0.2.0",
        };

        // Delivered at initialize, before any tool call - the one channel
        // guaranteed to reach every agent. Grown in step with the surface.
        options.ServerInstructions =
            "Recipe data scraped from UK meal-kit sources. A null field means "
            + "the source did not publish that value - never zero or none; "
            + "filters therefore exclude recipes missing the filtered value. "
            + "Offered portion counts vary by source and recipe: check "
            + "list_sources, and on a wrong portion count get_recipe fails "
            + "naming the counts that work. Search results are one page, not "
            + "the world - total reports the full match count, and nutrition "
            + "on each summary is per portion. Allergens split into confirmed "
            + "contains and may-contain-traces; excludeAllergens matches both "
            + "unless excludeTraces is set false. A source with "
            + "hasTraceAllergens false publishes no traces data, so its empty "
            + "traceAllergens means unknown, not none.";
    })
    .WithHttpTransport()
    .WithTools<RecipeTools>();

var app = builder.Build();

// First in the pipeline: everything downstream reads the corrected scheme and
// host off the request.
app.UseForwardedHeaders();

// The scraper owns the migrations. This host only reads, but it does own the
// cross-source views, which are derived from the registered sources rather than
// migrated - see UnionViewBuilder.
await app.Services.GetRequiredService<UnionViewBuilder>().BuildAsync(app.Services);

app.MapMcp("/mcp");
app.MapHealthChecks("/health");

await app.RunAsync();

/// <summary>Named so WebApplicationFactory can host the server in tests.</summary>
public partial class Program;
