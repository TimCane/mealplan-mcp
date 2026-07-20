namespace Mealplan.Infrastructure.Audit;

public enum AuditCallKind
{
    Tool,
    Prompt,
}

/// <summary>
/// What the transport knows about a caller: no auth exists, so identity is
/// the MCP session plus a daily-salted address hash. Across days, linking a
/// person is impossible by construction - see <see cref="AuditIpHasher"/>.
/// </summary>
public sealed record AuditSession(
    string Id,
    DateTimeOffset FirstSeenAt,
    string? ClientName,
    string? ClientVersion,
    string? UserAgent,
    string? IpHash);

/// <summary>
/// One tool or prompt call. ResultCount is the returned page's row count -
/// null for single-object results - so zero-result searches, the strongest
/// surface-improvement signal, are queryable directly.
/// </summary>
public sealed record AuditCall(
    Guid Id,
    AuditCallKind Kind,
    string Name,
    string? ArgsJson,
    DateTimeOffset CalledAt,
    int DurationMs,
    int? ResultCount,
    bool IsError,
    string? ErrorKind);

/// <summary>
/// The session identity rides on every entry so the writer can upsert it in
/// the same round trip - first contact wins, no in-memory session registry.
/// </summary>
public sealed record AuditEntry(AuditSession Session, AuditCall Call);
