using System.Threading.RateLimiting;
using Mealplan.Infrastructure;
using Mealplan.Infrastructure.Audit;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Mcp;
using Mealplan.Mcp.Audit;
using Mealplan.Mcp.Prompts;
using Mealplan.Mcp.Tools;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMealplanInfrastructure(builder.Configuration);
builder.Services.AddMealplanAudit(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddScoped<PromptCompletions>();
builder.Services.AddSingleton<McpCallAuditor>();

// The audit's address hash and the rate limiter's partitions both read the
// connection's remote address, which forwarding must first correct.
builder.Services.AddHttpContextAccessor();

// The proxy terminates TLS and forwards plain HTTP, so without these the app
// takes every request for http:// on an internal host and any absolute URL it
// builds points somewhere unreachable. OAuth metadata is the case that breaks
// outright: a resource URI advertised as http:// is rejected by MCP clients.
// XForwardedFor makes RemoteIpAddress the real client rather than the proxy -
// without it every caller would share one rate limit partition and one hash.
// The allowlist is cleared because the proxy's address on the Docker network is
// not stable, and nothing else can reach this container to spoof the headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The server is public and unauthenticated, and every search reaches
// Postgres. A generous fixed window per client address means no real agent
// ever sees a 429 while a misbehaving loop cannot hammer the database.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = http.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>().Value.PermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "mealplan",
            Version = "0.3.0",
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
            + "traceAllergens means unknown, not none. Filter slugs are per "
            + "source: resolve allergens with list_allergens, cuisines and "
            + "tags with list_cuisines and list_tags, and ingredient names "
            + "with search_ingredients before filtering - a guessed slug "
            + "silently matches nothing. Dislikes go through "
            + "excludeIngredients, where any match excludes. For shopping, "
            + "get_shopping_list returns one row per ingredient per recipe "
            + "with pantry staples flagged as not in the box; it never merges "
            + "rows across recipes or sources - combine duplicates yourself. "
            + "The plan_week, find_recipe and whats_available prompts encode "
            + "these flows end to end.";
    })
    .WithHttpTransport()
    .WithTools<RecipeTools>()
    .WithPrompts<PlanningPrompts>()
    .WithCompleteHandler(async (context, ct) =>
        await context.Services!.GetRequiredService<PromptCompletions>()
            .CompleteAsync(context.Params, ct))

    // The usage audit captures once here rather than per tool. Completions
    // stay unlogged: per-keystroke volume, partial typing, no insight.
    .WithRequestFilters(filters => filters
        .AddCallToolFilter(next => (context, ct) =>
            context.Services!.GetRequiredService<McpCallAuditor>()
                .CallToolAsync(next, context, ct))
        .AddGetPromptFilter(next => (context, ct) =>
            context.Services!.GetRequiredService<McpCallAuditor>()
                .GetPromptAsync(next, context, ct)));

var app = builder.Build();

// First in the pipeline: everything downstream reads the corrected scheme,
// host and client address off the request - the rate limiter partitions on
// the address, so it must run after the correction.
app.UseForwardedHeaders();
app.UseRateLimiter();

// The scraper owns the migrations. This host only reads, but it does own the
// cross-source views, which are derived from the registered sources rather than
// migrated - see UnionViewBuilder - and the audit tables, which no other host
// touches.
await app.Services.GetRequiredService<UnionViewBuilder>().BuildAsync(app.Services);
await AuditSchema.EnsureAsync(app.Services);

app.MapMcp("/mcp");
app.MapHealthChecks("/health");

await app.RunAsync();

/// <summary>Named so WebApplicationFactory can host the server in tests.</summary>
public partial class Program;
