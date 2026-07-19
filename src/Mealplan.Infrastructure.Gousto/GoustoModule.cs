using Mealplan.Infrastructure.Gousto.Persistence;
using Mealplan.Infrastructure.Http;
using Mealplan.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure.Gousto;

public class GoustoModule : ISourceModule
{
    private const string DefaultBaseAddress = "https://production-api.gousto.co.uk/cmsreadbroker/v1/";

    public string Source => GoustoSchema.SourceSlug;

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mealplan")
            ?? throw new InvalidOperationException("Connection string 'Mealplan' is not configured.");

        services.AddDbContext<GoustoDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", GoustoDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        var baseAddress = configuration[$"Sources:{Source}:BaseAddress"] ?? DefaultBaseAddress;

        // Politeness settings come from SourceOptions via this helper, so the
        // crawler cannot opt out of them.
        services.AddSourceHttpClient(Source, client => client.BaseAddress = new Uri(baseAddress));
    }
}
