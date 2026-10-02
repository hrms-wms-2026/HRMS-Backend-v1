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
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;

/// <summary>
/// What is coming up for one employee in the next N days: calendar events (recurring series
/// expanded like GetCalendarEventsQueryHandler; private events are excluded), approved leave, and
/// release reminders addressed to the employee. Earliest first, at most `Limit` (default 10, max 50);
/// Total counts every item in the window. Participant names are resolved only for the returned items.
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
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public async Task<Result<EmployeeUpcomingResponse>> Handle(GetEmployeeUpcomingQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeUpcomingResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Days < 1 || request.Days > MaxDays)
            return Result<EmployeeUpcomingResponse>.Failure($"days must be between 1 and {MaxDays}.");

        if (request.Limit < 1 || request.Limit > MaxLimit)
            return Result<EmployeeUpcomingResponse>.Failure($"limit must be between 1 and {MaxLimit}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeUpcomingResponse>.NotFound("The employee or selected organization record could not be found.");

        var from = clock.UtcNow;
        var to = from.AddDays(request.Days);
        var fromDate = clock.Today;
        var toDate = fromDate.AddDays(request.Days);
        // EventId (null for leave/releases) is kept beside each item so participants are only
        // resolved for the few events that survive the earliest-first cut.
        var items = new List<(EmployeeUpcomingItem Item, Guid? EventId)>();

        foreach (var e in await events.GetInDateRangeForEmployeeAsync(tenantId, request.EmployeeId, from, to, ct))
        {
            if (e.IsPrivate || e.IsRecurrenceCancelled) continue;
            items.Add((CalendarItem(e, e.StartDate, e.EndDate), e.Id));
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
                items.Add((CalendarItem(master, start, start + duration), master.Id));
            }
        }

        var leaveRows = await leave.ListOwnAsync(
            tenantId, request.EmployeeId, new LeaveRequestListFilter("approved", fromDate, toDate, null), ct);
        items.AddRange(leaveRows.Select(r => (new EmployeeUpcomingItem(
            "leave", r.LeaveTypeName, r.Request.StartAt, r.Request.EndAt, true, "Approved",
            Hours: r.Request.TotalHours), (Guid?)null)));

        var releaseRows = await releases.ListForRecipientAsync(tenantId, employee.UserId, fromDate, toDate, ct);
        items.AddRange(releaseRows.Select(r => (new EmployeeUpcomingItem(
            "release", r.VersionName,
            new DateTimeOffset(r.ScheduledDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            null, true, r.ProjectName), (Guid?)null)));

        var earliest = items.OrderBy(i => i.Item.Start).Take(request.Limit).ToList();
        var withParticipants = await AttachParticipantsAsync(tenantId, earliest, ct);
        return Result<EmployeeUpcomingResponse>.Success(new EmployeeUpcomingResponse(fromDate, toDate, withParticipants, items.Count));
    }

    private static EmployeeUpcomingItem CalendarItem(CalendarEvent e, DateTimeOffset start, DateTimeOffset end) =>
        new("calendar", e.Title, start, end, e.IsAllDay, e.Location,
            Description: e.Description, MeetingLink: e.MeetingLink, OrganizerName: e.OrganizerName, Timezone: e.Timezone);

    private async Task<IReadOnlyList<EmployeeUpcomingItem>> AttachParticipantsAsync(
        Guid tenantId, IReadOnlyList<(EmployeeUpcomingItem Item, Guid? EventId)> items, CancellationToken ct)
    {
        var eventIds = items.Where(i => i.EventId.HasValue).Select(i => i.EventId!.Value).Distinct().ToList();
        if (eventIds.Count == 0) return items.Select(i => i.Item).ToList();

        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, eventIds, ct);
        var names = new Dictionary<Guid, string>();
        foreach (var participant in participantsByEvent.Values.SelectMany(p => p))
        {
            if (names.ContainsKey(participant.EmployeeId)) continue;
            var person = await employees.GetByIdAsync(tenantId, participant.EmployeeId, ct);
            names[participant.EmployeeId] = person is null ? "Unknown" : $"{person.FirstName} {person.LastName}".Trim();
        }

        return items.Select(i =>
            i.EventId is { } id && participantsByEvent.TryGetValue(id, out var participants)
                ? i.Item with { Participants = participants.Select(p => names[p.EmployeeId]).ToList() }
                : i.Item).ToList();
    }
}
