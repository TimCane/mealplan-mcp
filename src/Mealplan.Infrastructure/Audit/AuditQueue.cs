using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Mealplan.Infrastructure.Audit;

/// <summary>
/// Hand-off between the request path and the background writer. Bounded and
/// drop-on-full: a slow database loses analytics rows, never delays a call.
/// </summary>
public class AuditQueue(IOptions<AuditOptions> options)
{
    private readonly Channel<AuditEntry> channel =
        Channel.CreateBounded<AuditEntry>(
            new BoundedChannelOptions(options.Value.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            });

    /// <summary>Never blocks; a full queue silently drops the entry.</summary>
    public void Write(AuditEntry entry) => channel.Writer.TryWrite(entry);

    public ChannelReader<AuditEntry> Reader => channel.Reader;
}
