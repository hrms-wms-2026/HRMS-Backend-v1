using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

/// <summary>What a conflict row is about (meeting availability warnings, 2026-10-28 design 4.2).</summary>
public static class CalendarConflictKinds
{
    public const string Event = "event";
    public const string Leave = "leave";
    public const string Holiday = "holiday";
    public const string OutsideHours = "outside_hours";
}

public sealed record CalendarConflict(
    Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle,
    DateTimeOffset OverlapStart, DateTimeOffset OverlapEnd, bool IsBusyOnly = false, string Kind = CalendarConflictKinds.Event);
public sealed record CalendarConflictsResponse(IReadOnlyList<CalendarConflict> Conflicts);

/// <summary><paramref name="TimeZone"/> is the meeting's zone, used for a participant whose own zone is unknown;
/// <paramref name="ExcludeMeetingSeriesId"/> is the meeting being edited, whose occurrences are not conflicts.</summary>
public sealed record CheckCalendarConflictsQuery(
    IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay = false, string? TimeZone = null, Guid? ExcludeMeetingSeriesId = null)
    : IRequest<Result<CalendarConflictsResponse>>;
