using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed record MeetingAttendeeItem(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, int? DurationSeconds);

/// <summary><see cref="SyncStatus"/>: "none" (no online meeting), "pending" (not fetched yet), or the
/// meeting's attendance_sync_status. "attendance_unavailable" means no data was received - not that
/// nobody attended.</summary>
public sealed record GetEventMeetingAttendanceResult(bool Available, IReadOnlyList<MeetingAttendeeItem> Attendees, string SyncStatus);

public sealed record GetEventMeetingAttendanceQuery(Guid EventId) : IRequest<Result<GetEventMeetingAttendanceResult>>;
