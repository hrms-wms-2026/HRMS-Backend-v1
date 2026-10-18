using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

/// <summary>
/// The meeting half of a materialized occurrence, 1:1 with its <c>tasks</c> row (task_kind = 'meeting'). The task
/// holds the title and agenda; this row holds time, location, privacy, cancellation, the link it uses and its
/// attendance progress (spec 4.1, 4.2). (<see cref="SeriesId"/>, <see cref="OriginalStart"/>) is the occurrence's
/// key and never changes (spec 5.6).
/// </summary>
public class MeetingDetails : ITenantOwnedEntity
{
    public Guid TaskId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SeriesId { get; set; }
    public DateTimeOffset OriginalStart { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }
    public bool IsPrivate { get; set; }
    /// <summary>Edited on its own; a series-wide edit no longer touches it.</summary>
    public bool IsDetached { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    /// <summary>Optimistic concurrency token; bumped on every occurrence write.</summary>
    public int Revision { get; set; }
    public Guid? CalendarEventMeetingId { get; set; }
    public string? AttendanceStatus { get; set; }
    public DateTimeOffset? AttendanceNextCheckAt { get; set; }
    public int AttendanceCheckpoint { get; set; }
    public DateTimeOffset? AttendanceFinalizedAt { get; set; }
    public string? AttendanceLastError { get; set; }
}
