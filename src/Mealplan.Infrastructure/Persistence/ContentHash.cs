using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mealplan.Infrastructure.Persistence;

/// <summary>
/// Change detection for raw payloads.
/// </summary>
public static class ContentHash
{
    /// <summary>
    /// SHA-256 over the payload with insignificant whitespace removed, so a
    /// source reformatting its JSON does not read as a content change. Property
    /// order is left alone - it is not insignificant in every API, and a source
    /// that genuinely reorders keys is worth re-normalising.
    /// </summary>
    public static byte[] Compute(string payload)
    {
        var canonical = Canonicalize(payload);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private static string Canonicalize(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            // Not JSON, or malformed. Hash it verbatim rather than throwing -
            // the store's job is to record what arrived, not to validate it.
            return payload;
        }
    }
}
