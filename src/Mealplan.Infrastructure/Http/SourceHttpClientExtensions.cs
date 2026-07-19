using Mealplan.Infrastructure.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Mealplan.Infrastructure.Http;

public static class SourceHttpClientExtensions
{
    /// <summary>
    /// Registers a named HttpClient carrying one source's politeness settings:
    /// browser user agent, throttling with jitter, and retry with exponential
    /// backoff. Sources call this rather than configuring resilience themselves,
    /// so a new source cannot accidentally crawl fast.
    /// </summary>
    public static IHttpClientBuilder AddSourceHttpClient(
        this IServiceCollection services,
        string source,
        Action<HttpClient>? configure = null)
    {
        var builder = services
            .AddHttpClient(source, (provider, client) =>
            {
                var settings = Settings(provider, source);

                client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
                client.Timeout = TimeSpan.FromMinutes(2);

                configure?.Invoke(client);
            })
            .AddHttpMessageHandler(provider =>
            {
                var settings = Settings(provider, source);
                var clock = provider.GetRequiredService<TimeProvider>();

                return new ThrottlingHandler(settings.RequestDelay, settings.RequestJitter, clock);
            });

        builder.AddStandardResilienceHandler().Configure((resilience, provider) =>
        {
            var settings = Settings(provider, source);

            resilience.Retry.MaxRetryAttempts = settings.MaxRetries;
            resilience.Retry.MaxDelay = settings.MaxRetryDelay;
            resilience.Retry.UseJitter = true;

            // A slow crawl is the point; tripping a circuit breaker on a
            // deliberately paced request stream would only get in the way.
            resilience.CircuitBreaker.MinimumThroughput = 100;
            resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);

            // Total must cover every attempt plus the backoff between them, or
            // the pipeline rejects its own configuration at startup.
            resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(30);
        });

        return builder;
    }

    private static SourceOptions Settings(IServiceProvider provider, string source) =>
        provider.GetRequiredService<IOptionsMonitor<SourceOptions>>().Get(source);
}
