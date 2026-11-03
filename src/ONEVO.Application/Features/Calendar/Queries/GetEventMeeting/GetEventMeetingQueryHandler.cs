using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeeting;

public sealed class GetEventMeetingQueryHandler(
    ICurrentUser currentUser, ICalendarEventRepository events, ICalendarEventMeetingRepository meetings, EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees)
    : IRequestHandler<GetEventMeetingQuery, Result<EventMeetingStatusResult>>
{
    public async Task<Result<EventMeetingStatusResult>> Handle(GetEventMeetingQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EventMeetingStatusResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var evt = await events.GetByIdForTenantAsync(tenantId, request.EventId, ct);
        if (evt is null)
            return Result<EventMeetingStatusResult>.NotFound("Calendar event not found.");
        if (evt.CreatedById != currentUser.UserId)
            return Result<EventMeetingStatusResult>.Forbidden("Only the event organizer can see its meeting status.");

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, evt.Id, ct);
        if (snapshot is null)
            return Result<EventMeetingStatusResult>.Success(new EventMeetingStatusResult("none", null, null, null));

        snapshot = await links.ExpireIfEndedAsync(evt, snapshot, ct);
        if (snapshot.Status == CalendarEventMeetingStatuses.Provisioning)
        {
            var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
            snapshot = await links.RecoverStaleProvisioningAsync(
                evt, snapshot, organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}", ct);
        }
        return Result<EventMeetingStatusResult>.Success(new EventMeetingStatusResult(
            snapshot.Status, snapshot.Provider,
            snapshot.Status == CalendarEventMeetingStatuses.Active ? snapshot.JoinUrl : null,
            snapshot.FailureReason));
    }
}
