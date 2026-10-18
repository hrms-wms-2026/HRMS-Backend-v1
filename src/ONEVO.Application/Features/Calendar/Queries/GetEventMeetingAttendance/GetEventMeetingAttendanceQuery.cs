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
}

public sealed record GetEventMeetingAttendanceQuery(Guid EventId) : IRequest<Result<GetEventMeetingAttendanceResult>>;
