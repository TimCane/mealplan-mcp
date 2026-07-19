using Mealplan.Infrastructure.HelloFresh.Api;
using Mealplan.Infrastructure.HelloFresh.Persistence;
using Mealplan.Infrastructure.Http;
using Mealplan.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure.HelloFresh;

public class HelloFreshModule : ISourceModule
{
    private const string DefaultBaseAddress = "https://www.hellofresh.co.uk/gw/recipes/recipes/";
    private const string DefaultSiteAddress = "https://www.hellofresh.co.uk/";

    public string Source => HelloFreshSchema.SourceSlug;

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mealplan")
            ?? throw new InvalidOperationException("Connection string 'Mealplan' is not configured.");

        services.AddDbContext<HelloFreshDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", HelloFreshDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        // The token is cached across requests and refreshed on expiry, so the
        // provider outlives any one crawl.
        services.AddOptions<HelloFreshOptions>()
            .Bind(configuration.GetSection(HelloFreshOptions.SectionName));

        services.AddSingleton<HelloFreshTokenProvider>();
        services.AddTransient<HelloFreshAuthHandler>();

        var siteAddress = configuration[$"Sources:{Source}:SiteAddress"] ?? DefaultSiteAddress;
        var baseAddress = configuration[$"Sources:{Source}:BaseAddress"] ?? DefaultBaseAddress;

        // Reads the website for a token. Throttled like any other request, and
        // deliberately not carrying the bearer it is fetching.
        services.AddSourceHttpClient(
            HelloFreshSchema.TokenClientName,
            client => client.BaseAddress = new Uri(siteAddress));

        services
            .AddSourceHttpClient(Source, client =>
            {
                client.BaseAddress = new Uri(baseAddress);

                // The API rejects requests without this; it identifies the
                // website's own recipe browser.
                client.DefaultRequestHeaders.Add("x-requested-by", "organic-growth");
            })
            .AddHttpMessageHandler<HelloFreshAuthHandler>();
    }
}
