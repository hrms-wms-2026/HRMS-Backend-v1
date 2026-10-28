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

public sealed record CheckCalendarConflictsQuery(
    IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate)
    : IRequest<Result<CalendarConflictsResponse>>;
