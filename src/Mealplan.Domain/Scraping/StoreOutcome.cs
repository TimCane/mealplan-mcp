namespace Mealplan.Domain.Scraping;

/// <summary>
/// What storing a raw document did. Only <see cref="Unchanged"/> avoids queueing
/// normalisation, which is the whole point of hashing payloads.
/// </summary>
public enum StoreOutcome
{
    /// <summary>First time this source key has been seen.</summary>
    Inserted = 1,

    /// <summary>Seen before, but the payload differs. A new version was written.</summary>
    Versioned = 2,

    /// <summary>Identical payload to the latest version. Only last seen moved.</summary>
    Unchanged = 3,
}
