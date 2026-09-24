using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;

public sealed class RemoveEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveEventMeetingCommand, Result>
{
    public async Task<Result> Handle(RemoveEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event organizer can remove its meeting.");

        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, request.EventId, ct);
        if (meeting is null)
            return Result.Success(); // nothing to remove - not an error

        await CancelRemoteMeetingAsync(tenantId, meeting, ct);

        meeting.Status = CalendarEventMeetingStatuses.Cancelled;
        meetings.Update(meeting);
        existing.MeetingLink = null;
        events.Update(existing);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task CancelRemoteMeetingAsync(Guid tenantId, CalendarEventMeeting meeting, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return; // connection was disconnected since the meeting was created - nothing to cancel remotely

        var isZoom = meeting.Provider == CalendarEventMeetingProviders.Zoom;
        var oauthProvider = isZoom ? "zoom" : "microsoft";
        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return; // reauth required - the remote meeting is orphaned but the local link is still cleared below

        if (isZoom)
            await zoomClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
        else
            await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
    }
}
