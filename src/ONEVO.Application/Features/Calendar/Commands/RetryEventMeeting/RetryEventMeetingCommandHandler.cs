using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RetryEventMeeting;

public sealed class RetryEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarEventMeetingRepository meetings,
    EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    IDateTimeProvider clock)
    : IRequestHandler<RetryEventMeetingCommand, Result<CreateEventMeetingResult>>
{
    public async Task<Result<CreateEventMeetingResult>> Handle(RetryEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CreateEventMeetingResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var evt = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (evt is null)
            return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");
        if (evt.CreatedById != currentUser.UserId)
            return Result<CreateEventMeetingResult>.Forbidden("Only the event organizer can retry its meeting.");

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, evt.Id, ct);
        if (snapshot is null)
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_not_retryable");
        var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
        snapshot = await links.ExpireIfEndedAsync(evt, snapshot, ct);
        snapshot = await links.RecoverStaleProvisioningAsync(evt, snapshot, organizerName, ct);
        if (snapshot.Status == CalendarEventMeetingStatuses.Unresolved)
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_unresolved");
        if (snapshot.Status != CalendarEventMeetingStatuses.ProvisioningUnknown)
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_not_retryable");

        // Recovery must use the connection that made the first attempt: createOrGet / the Zoom token are per organizer account.
        var connection = await connections.GetByIdForTenantAsync(tenantId, snapshot.ExternalCalendarConnectionId, ct);
        if (connection is null || connection.Status != ExternalCalendarConnectionStatuses.Active)
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");

        if (!await meetings.TryClaimUnknownAsync(tenantId, snapshot.Id, clock.UtcNow, ct))
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");

        var claimed = (await meetings.GetTrackedByCalendarEventAsync(tenantId, evt.Id, ct))!;
        return await links.ProvisionClaimedAsync(evt, claimed, connection, recovery: true, organizerName, ct);
    }
}
