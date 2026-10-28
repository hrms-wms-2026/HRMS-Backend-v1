using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

public static class MeetingSuppressionReasons
{
    public const string MovedSingle = "moved_single";
}

/// <summary>
/// Tombstone for a series slot whose occurrence moved to another series (spec 5.5): the slot is never materialized
/// again. Rows are never deleted.
/// </summary>
public class MeetingOccurrenceSuppression : ITenantOwnedEntity
{
    public Guid SeriesId { get; set; }
    public DateTimeOffset OriginalStart { get; set; }
    public Guid TenantId { get; set; }
    public string Reason { get; set; } = MeetingSuppressionReasons.MovedSingle;
    public Guid MovedToTaskId { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
