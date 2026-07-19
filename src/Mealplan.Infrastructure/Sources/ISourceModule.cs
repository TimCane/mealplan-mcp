using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure.Sources;

/// <summary>
/// A source's own service registration - its DbContext, its HttpClient, anything
/// else it needs. Discovered by the same scan as the other contracts, so a new
/// source still costs no edit to a host.
/// </summary>
/// <remarks>
/// Must have a parameterless constructor: this runs while the container is being
/// built, so nothing can be injected into it.
/// </remarks>
public interface ISourceModule
{
    string Source { get; }

    void Register(IServiceCollection services, IConfiguration configuration);
}
