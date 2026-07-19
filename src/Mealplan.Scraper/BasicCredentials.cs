using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Mealplan.Scraper;

/// <summary>
/// Reads an HTTP basic Authorization header and checks it against the expected
/// credentials.
/// </summary>
public static class BasicCredentials
{
    public static bool Match(string? header, JobsDashboardOptions expected)
    {
        // An unset expectation matches nothing. Options validation should have
        // stopped the host first, so this only guards a misuse.
        if (string.IsNullOrEmpty(expected.Username) || string.IsNullOrEmpty(expected.Password))
        {
            return false;
        }

        if (!AuthenticationHeaderValue.TryParse(header, out var parsed)
            || !string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || parsed.Parameter is null)
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        // Both halves are always compared, and compared in fixed time, so neither
        // a wrong username nor a wrong password is distinguishable by timing.
        var username = FixedTimeEquals(decoded[..separator], expected.Username);
        var password = FixedTimeEquals(decoded[(separator + 1)..], expected.Password);

        return username && password;
    }

    private static bool FixedTimeEquals(string candidate, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(expected));
}
