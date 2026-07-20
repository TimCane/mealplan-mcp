using System.Text.Json;
using Mealplan.Infrastructure.Audit;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Mealplan.Mcp.Audit;

/// <summary>
/// Captures every tool and prompt call for the usage audit, wrapped around
/// the handlers as request filters. Capture is a non-blocking write to a
/// bounded queue: a failing or slow audit path drops rows, never a request.
/// Completions are not captured - per-keystroke volume, no insight.
/// </summary>
public class McpCallAuditor(
    AuditQueue queue,
    AuditIpHasher hasher,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider time)
{
    public async ValueTask<CallToolResult> CallToolAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken ct)
    {
        var calledAt = time.GetUtcNow();
        var started = time.GetTimestamp();
        var name = context.Params?.Name ?? "(unknown)";
        var args = Args(context.Params?.Arguments);

        try
        {
            var result = await next(context, ct);

            Record(
                context.Server, AuditCallKind.Tool, name, args, calledAt, started,
                ResultCount(result),
                isError: result.IsError == true,
                errorKind: result.IsError == true ? "tool_error" : null);

            return result;
        }
        catch (Exception ex)
        {
            Record(
                context.Server, AuditCallKind.Tool, name, args, calledAt, started,
                resultCount: null, isError: true, errorKind: ex.GetType().Name);

            throw;
        }
    }

    public async ValueTask<GetPromptResult> GetPromptAsync(
        McpRequestHandler<GetPromptRequestParams, GetPromptResult> next,
        RequestContext<GetPromptRequestParams> context,
        CancellationToken ct)
    {
        var calledAt = time.GetUtcNow();
        var started = time.GetTimestamp();
        var name = context.Params?.Name ?? "(unknown)";
        var args = Args(context.Params?.Arguments);

        try
        {
            var result = await next(context, ct);

            Record(
                context.Server, AuditCallKind.Prompt, name, args, calledAt, started,
                resultCount: null, isError: false, errorKind: null);

            return result;
        }
        catch (Exception ex)
        {
            Record(
                context.Server, AuditCallKind.Prompt, name, args, calledAt, started,
                resultCount: null, isError: true, errorKind: ex.GetType().Name);

            throw;
        }
    }

    private void Record(
        McpServer server,
        AuditCallKind kind,
        string name,
        string? argsJson,
        DateTimeOffset calledAt,
        long started,
        int? resultCount,
        bool isError,
        string? errorKind)
    {
        queue.Write(new AuditEntry(
            Session(server, calledAt),
            new AuditCall(
                Guid.CreateVersion7(),
                kind,
                name,
                argsJson,
                calledAt,
                (int)time.GetElapsedTime(started).TotalMilliseconds,
                resultCount,
                isError,
                errorKind)));
    }

    private AuditSession Session(McpServer server, DateTimeOffset seenAt)
    {
        var http = httpContextAccessor.HttpContext;
        var address = http?.Connection.RemoteIpAddress?.ToString();
        var userAgent = http?.Request.Headers.UserAgent.ToString();

        return new AuditSession(
            server.SessionId ?? "stateless",
            seenAt,
            server.ClientInfo?.Name,
            server.ClientInfo?.Version,
            string.IsNullOrEmpty(userAgent) ? null : userAgent,
            address is null ? null : hasher.Hash(address));
    }

    /// <summary>
    /// Args are stored verbatim: the daily salt rotation means the
    /// health-adjacent filters cannot be tied to a person beyond one day.
    /// </summary>
    private static string? Args(IDictionary<string, JsonElement>? arguments) =>
        arguments is { Count: > 0 } ? JsonSerializer.Serialize(arguments) : null;

    /// <summary>
    /// The page row count for list-shaped results: paged envelopes carry
    /// items, bare lists arrive wrapped under result. Single-object results
    /// count as null - get_recipe is hit or miss, not zero rows.
    /// </summary>
    private static int? ResultCount(CallToolResult result)
    {
        if (result.StructuredContent is not { ValueKind: JsonValueKind.Object } json)
        {
            return null;
        }

        if (json.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array)
        {
            return items.GetArrayLength();
        }

        if (json.TryGetProperty("result", out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            return rows.GetArrayLength();
        }

        return null;
    }
}
