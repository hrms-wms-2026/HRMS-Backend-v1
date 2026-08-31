using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

public sealed record CalendarConflict(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle);
public sealed record CalendarConflictsResponse(IReadOnlyList<CalendarConflict> Conflicts);

public sealed record CheckCalendarConflictsQuery(
    IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate)
    : IRequest<Result<CalendarConflictsResponse>>;
