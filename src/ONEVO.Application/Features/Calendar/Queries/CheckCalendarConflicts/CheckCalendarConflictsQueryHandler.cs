using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

public sealed class CheckCalendarConflictsQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarRecurrenceExpander expander)
    : IRequestHandler<CheckCalendarConflictsQuery, Result<CalendarConflictsResponse>>
{
    public async Task<Result<CalendarConflictsResponse>> Handle(CheckCalendarConflictsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConflictsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var conflicts = new List<CalendarConflict>();

        foreach (var employeeId in request.ParticipantEmployeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;
            var employeeName = $"{employee.FirstName} {employee.LastName}";

            var realEvents = await events.GetInDateRangeForEmployeeAsync(tenantId, employeeId, request.StartDate, request.EndDate, ct);
            foreach (var e in realEvents)
                conflicts.Add(new CalendarConflict(employeeId, employeeName, e.Id, e.Title));

            var masters = await events.GetRecurringMastersForEmployeeAsync(tenantId, employeeId, request.EndDate, ct);
            foreach (var master in masters)
            {
                var occurrenceStarts = expander.Expand(master.RecurrenceRule!, master.StartDate, request.StartDate, request.EndDate);
                if (occurrenceStarts.Count == 0) continue;

                var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
                var hasUncancelledOccurrence = occurrenceStarts.Any(start =>
                    !children.Any(c => c.RecurrenceOriginalStart == start && c.IsRecurrenceCancelled));
                if (hasUncancelledOccurrence)
                    conflicts.Add(new CalendarConflict(employeeId, employeeName, master.Id, master.Title));
            }
        }

        return Result<CalendarConflictsResponse>.Success(new CalendarConflictsResponse(conflicts));
    }
}
