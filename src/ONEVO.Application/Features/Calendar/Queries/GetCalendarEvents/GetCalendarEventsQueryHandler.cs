using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed class GetCalendarEventsQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ICalendarEventRepository events)
    : IRequestHandler<GetCalendarEventsQuery, Result<CalendarEventsResponse>>
{
    public async Task<Result<CalendarEventsResponse>> Handle(GetCalendarEventsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);

        var rows = await events.GetInDateRangeForCallerAsync(
            tenantId, currentUser.UserId, employee?.Id, request.From, request.To, ct);

        var items = rows.Select(e => new CalendarEventItem(
            e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color,
            e.Recurrence, e.IsAllDay, e.Timezone, e.EventStatus, e.IsPrivate, e.Location,
            e.MeetingLink, e.ExternalSource, e.CreatedById)).ToList();

        return Result<CalendarEventsResponse>.Success(new CalendarEventsResponse(items));
    }
}
