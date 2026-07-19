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
    .AddMcpServer(options => options.ServerInfo = new()
    {
        Name = "mealplan",
        Version = "0.1.0",
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
