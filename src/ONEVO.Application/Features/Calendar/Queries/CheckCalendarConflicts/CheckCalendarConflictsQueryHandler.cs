using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

public sealed class CheckCalendarConflictsQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarRecurrenceExpander expander,
    MeetingCalendarProjection? meetings = null,
    IParticipantAvailabilityChecker? availability = null)
    : IRequestHandler<CheckCalendarConflictsQuery, Result<CalendarConflictsResponse>>
{
    public async Task<Result<CalendarConflictsResponse>> Handle(CheckCalendarConflictsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConflictsResponse>.Forbidden();

        // Npgsql writes only UTC DateTimeOffsets to timestamptz; clients may send local offsets (the meeting form does).
        request = request with { StartDate = request.StartDate.ToUniversalTime(), EndDate = request.EndDate.ToUniversalTime() };
        var tenantId = currentUser.TenantId;
        var conflicts = new List<CalendarConflict>();
        // Task meetings the caller cannot see themselves are reported as "Busy" with their time only (spec 5.1 privacy).
        HashSet<Guid>? visibleToCaller = null;
        var caller = meetings is null && availability is null
            ? null
            : await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (meetings is not null)
        {
            visibleToCaller = caller is null ? []
                : (await meetings.ListAsync(tenantId, caller.Id, request.StartDate, request.EndDate, ct)).Select(i => i.Id).ToHashSet();
        }

        var checkedEmployees = new List<ONEVO.Domain.Features.CoreHr.Entities.Employee>();
        foreach (var employeeId in request.ParticipantEmployeeIds.Distinct())
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;
            checkedEmployees.Add(employee);
            var employeeName = $"{employee.FirstName} {employee.LastName}";

            var realEvents = await events.GetInDateRangeForEmployeeAsync(tenantId, employeeId, request.StartDate, request.EndDate, ct);
            foreach (var e in realEvents)
            {
                var overlapStart = e.StartDate > request.StartDate ? e.StartDate : request.StartDate;
                var overlapEnd = e.EndDate < request.EndDate ? e.EndDate : request.EndDate;
                conflicts.Add(new CalendarConflict(employeeId, employeeName, e.Id, e.Title, overlapStart, overlapEnd));
            }

            var masters = await events.GetRecurringMastersForEmployeeAsync(tenantId, employeeId, request.EndDate, ct);
            foreach (var master in masters)
            {
                var occurrenceStarts = expander.Expand(master.RecurrenceRule!, master.StartDate, request.StartDate, request.EndDate);
                if (occurrenceStarts.Count == 0) continue;

                var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
                var hasUncancelledOccurrence = occurrenceStarts.Any(start =>
                    !children.Any(c => c.RecurrenceOriginalStart == start && c.IsRecurrenceCancelled));
                if (hasUncancelledOccurrence)
                {
                    var overlapStart = master.StartDate > request.StartDate ? master.StartDate : request.StartDate;
                    var overlapEnd = master.EndDate < request.EndDate ? master.EndDate : request.EndDate;
                    conflicts.Add(new CalendarConflict(employeeId, employeeName, master.Id, master.Title, overlapStart, overlapEnd));
                }
            }

            if (meetings is null)
                continue;
            foreach (var m in await meetings.ListAsync(tenantId, employeeId, request.StartDate, request.EndDate, ct))
            {
                if (m.StartDate >= request.EndDate || m.EndDate <= request.StartDate)
                    continue;
                if (request.ExcludeMeetingSeriesId is Guid excluded && m.MeetingSeriesId == excluded)
                    continue;
                var overlapStart = m.StartDate > request.StartDate ? m.StartDate : request.StartDate;
                var overlapEnd = m.EndDate < request.EndDate ? m.EndDate : request.EndDate;
                conflicts.Add(visibleToCaller!.Contains(m.Id)
                    ? new CalendarConflict(employeeId, employeeName, m.Id, m.Title, overlapStart, overlapEnd)
                    : new CalendarConflict(employeeId, employeeName, Guid.Empty, "Busy", overlapStart, overlapEnd, IsBusyOnly: true));
            }
        }

        if (availability is not null)
            conflicts.AddRange(await availability.CheckAsync(tenantId, caller?.Id, checkedEmployees, request.StartDate,
                request.EndDate, request.IsAllDay, request.TimeZone ?? "UTC", ct));

        return Result<CalendarConflictsResponse>.Success(new CalendarConflictsResponse(conflicts));
    }
}
