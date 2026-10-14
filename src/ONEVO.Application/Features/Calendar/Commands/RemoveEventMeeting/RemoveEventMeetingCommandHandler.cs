using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
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
    EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
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

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, request.EventId, ct);
        if (snapshot is null)
            return await ClearLocalLinkAsync(existing, ct); // a pasted link: nothing tracked remotely

        snapshot = await links.ExpireIfEndedAsync(existing, snapshot, ct);
        return snapshot.Status switch
        {
            CalendarEventMeetingStatuses.Active => await RemoveActiveAsync(existing, ct),
            CalendarEventMeetingStatuses.ProvisioningUnknown => await links.ResolveUnknownForRemovalAsync(existing, snapshot, ct),
            CalendarEventMeetingStatuses.Provisioning => Result.Conflict("meeting_link_in_progress"),
            CalendarEventMeetingStatuses.Unresolved => Result.Conflict("meeting_link_unresolved"),
            _ => await ClearLocalLinkAsync(existing, ct) // failed / cancelled: nothing exists remotely
        };
    }

    private async Task<Result> RemoveActiveAsync(CalendarEvent existing, CancellationToken ct)
    {
        var meeting = (await meetings.GetTrackedByCalendarEventAsync(existing.TenantId, existing.Id, ct))!;
        try
        {
            await CancelRemoteMeetingAsync(existing.TenantId, meeting, ct);
        }
        catch (ZoomMeetingInProgressException ex)
        {
            return Result.Conflict($"{ex.Message} Try again once the meeting has ended.");
        }
        catch (MeetingProviderException ex)
        {
            var provider = CalendarMeetingProviderNames.Display(meeting.Provider);
            return Result.Failure($"We couldn't remove the meeting in {provider} ({ex.Kind}). Try again.", 502);
        }

        var organizer = await employees.GetDefaultForUserAsync(existing.TenantId, currentUser.UserId, ct);
        var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            meeting.Status = CalendarEventMeetingStatuses.Cancelled;
            meetings.Update(meeting);
            existing.MeetingLink = null;
            // P1-4: people must not keep a dead join link.
            await links.SendLinkRemovedAsync(existing, meeting, organizerName, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }

    private async Task<Result> ClearLocalLinkAsync(CalendarEvent existing, CancellationToken ct)
    {
        if (existing.MeetingLink is not null)
        {
            existing.MeetingLink = null; // tracked: only meeting_link is written
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    private async Task CancelRemoteMeetingAsync(Guid tenantId, CalendarEventMeeting meeting, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return; // connection disconnected since: nothing we can cancel with

        var isZoom = meeting.Provider == CalendarEventMeetingProviders.Zoom;
        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, isZoom ? "zoom" : "microsoft", ct);
        if (accessToken is null)
            return; // re-auth required: the local link is still cleared

        if (isZoom)
            await zoomClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
        else
            await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
    }
}
