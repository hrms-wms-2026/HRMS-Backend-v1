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
    /// <summary>When the ended occurrence's person-hours were written to its task (once; see MeetingHoursStamper).</summary>
    public DateTimeOffset? HoursStampedAt { get; set; }
}

/// <summary>An occurrence's attendance progress (spec 5.8). It only moves forward and never leaves <see cref="Synced"/>.</summary>
public static class MeetingAttendanceStatuses
{
    /// <summary>The occurrence has a link; no provider session found yet.</summary>
    public const string Pending = "pending";
    /// <summary>Sessions found before the last checkpoint: later checks may still add rows.</summary>
    public const string Provisional = "provisional";
    /// <summary>Final: sessions found at or after the last checkpoint.</summary>
    public const string Synced = "synced";
    /// <summary>The provider answered but has no session for the occurrence.</summary>
    public const string Unavailable = "attendance_unavailable";
    /// <summary>The provider erred at the last checkpoint (the reason is kept).</summary>
    public const string SyncFailed = "sync_failed";
    /// <summary>The occurrence has no link.</summary>
    public const string NotApplicable = "not_applicable";
}
