using System.Security.Cryptography;
using System.Text;

namespace Mealplan.Infrastructure.Audit;

/// <summary>
/// Hashes caller addresses with a salt that rotates daily and lives only in
/// memory. Within a day the hash groups one caller's sessions; across days
/// linking is impossible by construction, because yesterday's salt is never
/// stored anywhere. That is why args can be kept verbatim.
/// </summary>
public class AuditIpHasher(TimeProvider time)
{
    private readonly Lock gate = new();
    private DateOnly day;
    private byte[] salt = [];

    public string Hash(string address)
    {
        byte[] currentSalt;

        lock (gate)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

            if (today != day || salt.Length == 0)
            {
                day = today;
                salt = RandomNumberGenerator.GetBytes(32);
            }

            currentSalt = salt;
        }

        return Convert.ToHexStringLower(
            SHA256.HashData([.. Encoding.UTF8.GetBytes(address), .. currentSalt]));
    }
}
