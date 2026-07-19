using Mealplan.Infrastructure;
using Mealplan.Infrastructure.Persistence;
using Mealplan.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMealplanInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

builder.Services
    .AddMcpServer(options => options.ServerInfo = new()
    {
        Name = "mealplan",
        Version = "0.1.0",
    })
    .WithHttpTransport()
    .WithTools<RecipeTools>();

var app = builder.Build();

// The scraper owns the migrations. This host only reads, but it does own the
// cross-source views, which are derived from the registered sources rather than
// migrated - see UnionViewBuilder.
await app.Services.GetRequiredService<UnionViewBuilder>().BuildAsync(app.Services);

app.MapMcp("/mcp");
app.MapHealthChecks("/health");

await app.RunAsync();
