namespace Mealplan.Domain.Sources;

/// <summary>
/// Where a source's normalised data lives and what it can offer. Keeps schema
/// naming and capability reporting next to the source that owns them rather than
/// in a table the whole system has to agree on.
/// </summary>
public interface ISourceSchema
{
    string Source { get; }

    /// <summary>Human-readable name for the MCP surface, e.g. "HelloFresh".</summary>
    string DisplayName { get; }

    /// <summary>Postgres schema holding this source's normalised tables.</summary>
    string SchemaName { get; }

    SourceCapabilities Capabilities { get; }
}
