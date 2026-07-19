namespace Mealplan.Infrastructure.Http;

/// <summary>
/// Keeps a minimum gap between requests on one source, with jitter so a crawl
/// does not arrive on a metronome. Serialises requests through a semaphore, so
/// the gap holds even if a crawler ever issues calls concurrently.
/// </summary>
public class ThrottlingHandler(TimeSpan delay, double jitter, TimeProvider clock) : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wait = NextWait();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, clock, cancellationToken);
            }

            _lastRequest = clock.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private TimeSpan NextWait()
    {
        var target = delay * (1 + ((Random.Shared.NextDouble() * 2 - 1) * jitter));
        var elapsed = clock.GetUtcNow() - _lastRequest;
        var remaining = target - elapsed;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
