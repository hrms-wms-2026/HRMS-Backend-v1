using MediatR;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed class GetCalendarEventsQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander)
    : IRequestHandler<GetCalendarEventsQuery, Result<CalendarEventsResponse>>
{
    private static readonly Guid OccurrenceIdNamespace = Guid.Parse("6f1f9b2a-6c1e-4b7a-9c2e-8f6a1d2b3c4d");

    public async Task<Result<CalendarEventsResponse>> Handle(GetCalendarEventsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);

        var realRows = await events.GetInDateRangeForCallerAsync(
            tenantId, currentUser.UserId, employee?.Id, request.From, request.To, ct);

        var items = realRows.Select(e => new CalendarEventItem(
            e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color,
            e.Recurrence, e.IsAllDay, e.Timezone, e.EventStatus, e.IsPrivate, e.Location,
            e.MeetingLink, e.ExternalSource, e.CreatedById)).ToList();

        var masters = await events.GetRecurringMastersForCallerAsync(tenantId, currentUser.UserId, employee?.Id, request.To, ct);

        foreach (var master in masters)
        {
            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
            var occurrenceStarts = expander.Expand(master.RecurrenceRule!, master.StartDate, request.From, request.To);
            var duration = master.EndDate - master.StartDate;

            foreach (var occurrenceStart in occurrenceStarts)
            {
                var overridden = children.Any(c => c.RecurrenceOriginalStart == occurrenceStart);
                if (overridden)
                    continue; // a detached row is already in realRows; a cancellation is never shown

                var occurrenceId = DeterministicGuid.Create(OccurrenceIdNamespace, $"{master.Id:N}|{occurrenceStart:O}");
                items.Add(new CalendarEventItem(
                    occurrenceId, master.Title, master.Description, occurrenceStart, occurrenceStart + duration,
                    master.SourceType, master.Color, master.Recurrence, master.IsAllDay, master.Timezone,
                    master.EventStatus, master.IsPrivate, master.Location, master.MeetingLink,
                    master.ExternalSource, master.CreatedById,
                    IsRecurringOccurrence: true, RecurrenceMasterId: master.Id, OriginalStart: occurrenceStart));
            }
        }

        return Result<CalendarEventsResponse>.Success(new CalendarEventsResponse(items.OrderBy(i => i.StartDate).ToList()));
    }
}
