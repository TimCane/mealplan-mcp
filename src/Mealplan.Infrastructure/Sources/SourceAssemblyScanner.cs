using System.Reflection;
using Mealplan.Domain.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mealplan.Infrastructure.Sources;

/// <summary>
/// Finds source implementations by loading Mealplan.Infrastructure.* assemblies
/// from the output directory.
/// </summary>
/// <remarks>
/// Scanning files rather than loaded assemblies is deliberate. A source project
/// is referenced but never referred to in code, so the runtime has no reason to
/// load it before the scan runs, and a host would otherwise need a line of
/// registration per source - exactly the coupling this design avoids.
/// </remarks>
public static class SourceAssemblyScanner
{
    private const string SourceAssemblyPrefix = "Mealplan.Infrastructure.";

    /// <summary>
    /// Registers every source implementation found on disk and returns the slugs
    /// discovered, so callers can bind per-source configuration without building
    /// an interim service provider.
    /// </summary>
    public static IReadOnlyList<string> AddSourcesFromAssemblies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var slugs = new List<string>();

        foreach (var assembly in DiscoverSourceAssemblies())
        {
            slugs.AddRange(RegisterImplementations(services, assembly));
            RegisterModules(services, configuration, assembly);
        }

        // Scoped, not singleton: it resolves crawlers and normalisers, which hold
        // a DbContext. A singleton here would capture the first request's scope.
        services.AddScoped<SourceRegistry>();

        return slugs;
    }

    private static IEnumerable<Assembly> DiscoverSourceAssemblies()
    {
        var directory = AppContext.BaseDirectory;

        foreach (var path in Directory.EnumerateFiles(directory, $"{SourceAssemblyPrefix}*.dll"))
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // A native or mixed-mode file that happens to match the pattern.
                continue;
            }

            yield return assembly;
        }
    }

    private static IEnumerable<string> RegisterImplementations(
        IServiceCollection services,
        Assembly assembly)
    {
        var contracts = new[]
        {
            typeof(ISourceCrawler),
            typeof(ISourceNormalizer),
            typeof(ISourceSchema),
        };

        var slugs = new List<string>();

        var types = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsPublic: true });

        foreach (var type in types)
        {
            foreach (var contract in contracts.Where(c => c.IsAssignableFrom(type)))
            {
                // Scoped: crawlers and normalisers take a DbContext.
                services.AddScoped(contract, type);
            }

            // Schemas are metadata and must be constructible without services,
            // so the slug is known before the container exists.
            if (typeof(ISourceSchema).IsAssignableFrom(type))
            {
                slugs.Add(DescribeSchema(type).Source);
            }
        }

        return slugs;
    }

    /// <summary>
    /// Lets each source register its own DbContext and HttpClient. Runs after
    /// the contracts so a module can assume nothing about ordering.
    /// </summary>
    private static void RegisterModules(
        IServiceCollection services,
        IConfiguration configuration,
        Assembly assembly)
    {
        var modules = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsPublic: true })
            .Where(typeof(ISourceModule).IsAssignableFrom);

        foreach (var type in modules)
        {
            var module = Activator.CreateInstance(type) as ISourceModule
                ?? throw new InvalidOperationException(
                    $"{type.FullName} implements ISourceModule but has no parameterless "
                    + "constructor. Modules run while the container is being built.");

            module.Register(services, configuration);
        }
    }

    private static ISourceSchema DescribeSchema(Type type)
    {
        return Activator.CreateInstance(type) as ISourceSchema
            ?? throw new InvalidOperationException(
                $"{type.FullName} implements ISourceSchema but has no parameterless constructor. "
                + "Schemas describe a source and must be constructible without services.");
    }
}
