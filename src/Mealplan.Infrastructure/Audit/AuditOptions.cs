using System.ComponentModel.DataAnnotations;

namespace Mealplan.Infrastructure.Audit;

/// <summary>
/// Usage audit tuning. The defaults match the design: rows live 90 days, and
/// the queue is deep enough that only a stalled database drops entries.
/// </summary>
public class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Days a row survives before the daily sweep removes it.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Entries held between capture and the background writer. When full, new
    /// entries are dropped - analytics rows are droppable, requests are not.
    /// </summary>
    [Range(1, 100_000)]
    public int QueueCapacity { get; set; } = 4096;
}
