using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;

/// <summary>
/// What is coming up for one employee in the next N days: calendar events (recurring series
/// expanded like GetCalendarEventsQueryHandler; private events are excluded), approved leave, and
/// release reminders addressed to the employee. Earliest first, at most 10.
/// </summary>
public sealed class GetEmployeeUpcomingQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander,
    ILeaveRequestRepository leave,
    IReleaseCalendarRepository releases,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeUpcomingQuery, Result<EmployeeUpcomingResponse>>
{
    public const int MaxDays = 60;
    public const int MaxItems = 10;

    public async Task<Result<EmployeeUpcomingResponse>> Handle(GetEmployeeUpcomingQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeUpcomingResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Days < 1 || request.Days > MaxDays)
            return Result<EmployeeUpcomingResponse>.Failure($"days must be between 1 and {MaxDays}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeUpcomingResponse>.NotFound("The employee or selected organization record could not be found.");

        var from = clock.UtcNow;
        var to = from.AddDays(request.Days);
        var fromDate = clock.Today;
        var toDate = fromDate.AddDays(request.Days);
        var items = new List<EmployeeUpcomingItem>();

        foreach (var e in await events.GetInDateRangeForEmployeeAsync(tenantId, request.EmployeeId, from, to, ct))
        {
            if (e.IsPrivate || e.IsRecurrenceCancelled) continue;
            items.Add(new EmployeeUpcomingItem("calendar", e.Title, e.StartDate, e.EndDate, e.IsAllDay, e.Location));
        }

        foreach (var master in await events.GetRecurringMastersForEmployeeAsync(tenantId, request.EmployeeId, to, ct))
        {
            if (master.IsPrivate || string.IsNullOrWhiteSpace(master.RecurrenceRule)) continue;

            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
            var duration = master.EndDate - master.StartDate;
            foreach (var start in expander.Expand(master.RecurrenceRule, master.StartDate, from, to))
            {
                // A detached (edited or cancelled) occurrence is represented by its own child row.
                if (children.Any(c => c.RecurrenceOriginalStart == start)) continue;
                items.Add(new EmployeeUpcomingItem("calendar", master.Title, start, start + duration, master.IsAllDay, master.Location));
            }
        }

        var leaveRows = await leave.ListOwnAsync(
            tenantId, request.EmployeeId, new LeaveRequestListFilter("approved", fromDate, toDate, null), ct);
        items.AddRange(leaveRows.Select(r => new EmployeeUpcomingItem(
            "leave", r.LeaveTypeName, r.Request.StartAt, r.Request.EndAt, true, "Approved")));

        var releaseRows = await releases.ListForRecipientAsync(tenantId, employee.UserId, fromDate, toDate, ct);
        items.AddRange(releaseRows.Select(r => new EmployeeUpcomingItem(
            "release", r.VersionName,
            new DateTimeOffset(r.ScheduledDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            null, true, r.ProjectName)));

        var earliest = items.OrderBy(i => i.Start).Take(MaxItems).ToList();
        return Result<EmployeeUpcomingResponse>.Success(new EmployeeUpcomingResponse(fromDate, toDate, earliest));
    }
}
