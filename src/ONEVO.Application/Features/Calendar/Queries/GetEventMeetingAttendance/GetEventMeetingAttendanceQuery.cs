using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed record MeetingAttendeeItem(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, int? DurationSeconds);

/// <summary>An incident the meeting raised for one participant (rule title and occurrence only - never the evidence behind
/// it); <see cref="Estimated"/> = detected from the desktop agent rather than the provider's attendance report.</summary>
public sealed record MeetingIncidentItem(string EmployeeName, string Rule, DateTimeOffset OccurrenceStart, bool Estimated);

/// <summary>The organizer's Attendance tab (plan FC) - anyone else is refused: the provider's report plus the incidents
/// the meeting raised.</summary>
public sealed record GetEventMeetingAttendanceResult(
    bool Available, IReadOnlyList<MeetingAttendeeItem> Attendees, IReadOnlyList<MeetingIncidentItem>? Incidents = null)
{
    public IReadOnlyList<MeetingIncidentItem> Incidents { get; init; } = Incidents ?? [];

    /// <summary>"none" (no online meeting), "pending" (not settled yet), "synced", "attendance_unavailable" (the provider
    /// never published a report) or "sync_failed" (it could not be fetched - see <see cref="SyncError"/>). The last two
    /// mean "unknown", never "nobody attended".</summary>
    public string SyncStatus { get; init; } = MeetingAttendanceSyncStatus.None;

    /// <summary>Why the sync failed, when <see cref="SyncStatus"/> is "sync_failed".</summary>
    public string? SyncError { get; init; }
}

public static class MeetingAttendanceSyncStatus
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Synced = "synced";
    public const string Unavailable = "attendance_unavailable";
    public const string Failed = "sync_failed";

    public static string Of(ONEVO.Domain.Features.Calendar.Entities.CalendarEventMeeting? meeting) => meeting switch
    {
        null => None,
        { LastAttendanceSyncedAt: not null } => Synced,
        { AttendanceSyncFailedAt: not null } => Failed,
        { AttendanceUnavailableAt: not null } => Unavailable,
        _ => Pending,
    };
}

public sealed record GetEventMeetingAttendanceQuery(Guid EventId) : IRequest<Result<GetEventMeetingAttendanceResult>>;
