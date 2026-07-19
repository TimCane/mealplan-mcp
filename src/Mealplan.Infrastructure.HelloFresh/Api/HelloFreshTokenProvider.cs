using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Mealplan.Infrastructure.HelloFresh.Api;

/// <summary>
/// The recipe API needs an anonymous bearer token. There is no token endpoint -
/// the website embeds one in its page data, so it is read from there.
/// </summary>
/// <remarks>
/// The token is anonymous: its claims are an issuer, an issued-at and an expiry,
/// with no user or account. Reading it is the same thing the site's own
/// JavaScript does before calling the same endpoint.
/// </remarks>
public partial class HelloFreshTokenProvider(
    IHttpClientFactory clients,
    TimeProvider clock,
    ILogger<HelloFreshTokenProvider> logger)
{
    /// <summary>Refresh this far ahead of expiry rather than at the cliff.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stillValid = _token is not null
                && clock.GetUtcNow() < _expiresAt - RefreshMargin;

            if (stillValid && !forceRefresh)
            {
                return _token!;
            }

            var fetched = await FetchAsync(ct);

            _token = fetched.Token;
            _expiresAt = fetched.ExpiresAt;

            logger.LogInformation(
                "HelloFresh token refreshed, expires {ExpiresAt:u}",
                fetched.ExpiresAt);

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Token, DateTimeOffset ExpiresAt)> FetchAsync(CancellationToken ct)
    {
        var client = clients.CreateClient(HelloFreshSchema.TokenClientName);

        using var response = await client.GetAsync("recipes", ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        var token = ExtractToken(html)
            ?? throw new InvalidOperationException(
                "No access_token found in the HelloFresh recipes page. The page shape "
                + "has probably changed; the crawler cannot authenticate without it.");

        return (token, ExpiryOf(token, clock.GetUtcNow()));
    }

    /// <summary>
    /// The token sits in a serverAuth object inside the page's embedded JSON.
    /// Matched by key rather than by walking the document, because the
    /// surrounding structure is a build detail that changes without notice.
    /// </summary>
    internal static string? ExtractToken(string html)
    {
        var match = AccessToken().Match(html);

        return match.Success ? match.Groups["token"].Value : null;
    }

    /// <summary>
    /// Reads the expiry from the token's own exp claim rather than trusting a
    /// separate expires_in field, so the two cannot disagree.
    /// </summary>
    internal static DateTimeOffset ExpiryOf(string jwt, DateTimeOffset now)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return now.AddDays(1);
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));

            return payload.RootElement.TryGetProperty("exp", out var exp)
                && exp.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : now.AddDays(1);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // Unreadable claims are not fatal: a short assumed life just means
            // refreshing more often than necessary.
            return now.AddDays(1);
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        return Convert.FromBase64String(padded);
    }

    [GeneratedRegex(
        "\"access_token\"\\s*:\\s*\"(?<token>[A-Za-z0-9\\-_]+\\.[A-Za-z0-9\\-_]+\\.[A-Za-z0-9\\-_]+)\"",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AccessToken();
}

/// <summary>Adds the bearer, refreshing once if the API rejects it.</summary>
public class HelloFreshAuthHandler(HelloFreshTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Authorize(request, forceRefresh: false, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode is not (System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden))
        {
            return response;
        }

        // One retry with a fresh token. If that is also rejected, the run should
        // fail rather than hammer the endpoint.
        response.Dispose();

        using var retry = await CloneAsync(request, cancellationToken);
        await Authorize(retry, forceRefresh: true, cancellationToken);

        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task Authorize(
        HttpRequestMessage request,
        bool forceRefresh,
        CancellationToken ct)
    {
        var token = await tokens.GetAsync(forceRefresh, ct);

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            token);
    }

    /// <summary>A sent request cannot be sent again, so the retry needs a copy.</summary>
    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var buffer = await request.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(buffer);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
