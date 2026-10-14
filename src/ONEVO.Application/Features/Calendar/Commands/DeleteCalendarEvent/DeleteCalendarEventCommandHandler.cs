using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;

public sealed class DeleteCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    ICalendarEventMeetingRepository meetings,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCalendarEventCommand, Result>
{
    public async Task<Result> Handle(DeleteCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event creator can delete this event.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Plan 0C (X-2) replaces this remote cancel with an after-commit outbox message.
            var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, innerCt);
            if (meeting is not null && meeting.Status == CalendarEventMeetingStatuses.Active)
            {
                var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, innerCt);
                if (connection is not null)
                {
                    var isZoom = meeting.Provider == CalendarEventMeetingProviders.Zoom;
                    var oauthProvider = isZoom ? "zoom" : "microsoft";
                    var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, innerCt);
                    if (accessToken is not null)
                    {
                        if (isZoom)
                            await zoomClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, innerCt);
                        else
                            await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, innerCt);
                    }
                }
            }

            // Load participants AND guests before the event is removed, and stage before the save.
            var participants = (await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt)).GetValueOrDefault(existing.Id) ?? [];
            var guests = (await events.GetGuestsForEventsAsync(tenantId, [existing.Id], innerCt)).GetValueOrDefault(existing.Id) ?? [];
            if (participants.Count > 0 || guests.Count > 0)
            {
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyEventCancelledAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Cancel(existing.Id, null, p.Id))).ToList(),
                    guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Cancel(existing.Id, null, g.Id))).ToList(),
                    organizerName, innerCt);
            }

            events.Remove(existing);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
