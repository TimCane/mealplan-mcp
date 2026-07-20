using FluentAssertions;
using Mealplan.Infrastructure.Audit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Mealplan.Tests;

/// <summary>
/// Identity degrades by design: within a day the hash groups one caller's
/// sessions, across days it cannot. That property is what lets the audit
/// store filter arguments verbatim, so it is pinned rather than assumed.
/// </summary>
public class AuditIdentityTests
{
    [Fact]
    public void One_address_hashes_the_same_way_all_day()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 20, 0, 5, 0, TimeSpan.Zero));
        var hasher = new AuditIpHasher(time);

        var first = hasher.Hash("203.0.113.7");
        time.Advance(TimeSpan.FromHours(23));

        hasher.Hash("203.0.113.7").Should().Be(first);
        hasher.Hash("203.0.113.8").Should().NotBe(first);
        first.Should().NotContain("203.0.113.7");
    }

    [Fact]
    public void The_same_address_hashes_differently_across_salt_days()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        var hasher = new AuditIpHasher(time);

        var today = hasher.Hash("203.0.113.7");
        time.Advance(TimeSpan.FromDays(1));

        hasher.Hash("203.0.113.7").Should().NotBe(today,
            "yesterday's salt is never stored, so linking a caller across days "
            + "is impossible by construction");
    }

    [Fact]
    public void A_full_queue_drops_entries_rather_than_blocking_the_caller()
    {
        var queue = new AuditQueue(Options.Create(new AuditOptions { QueueCapacity = 1 }));

        for (var i = 0; i < 1000; i++)
        {
            queue.Write(Entry());
        }

        queue.Reader.TryRead(out _).Should().BeTrue();
        queue.Reader.TryRead(out _).Should().BeFalse(
            "the overflow is dropped, not buffered - analytics rows are "
            + "droppable and requests are not");
    }

    private static AuditEntry Entry() => new(
        new AuditSession("session", DateTimeOffset.UnixEpoch, null, null, null, null),
        new AuditCall(
            Guid.CreateVersion7(), AuditCallKind.Tool, "search_recipes", null,
            DateTimeOffset.UnixEpoch, 1, 0, false, null));
}
