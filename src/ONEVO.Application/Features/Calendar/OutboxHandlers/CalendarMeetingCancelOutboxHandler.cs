using System.Text.Json;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

/// <summary>Everything the cancel needs after the event row is gone (spec X-2).</summary>
public sealed record CalendarMeetingCancelPayload(
    Guid TenantId, string Provider, string ExternalMeetingId, Guid ExternalCalendarConnectionId,
    Guid OrganizerUserId, Guid OrganizerEmployeeId, string EventTitle);

/// <summary>Cancels a deleted event's Teams/Zoom meeting after the delete committed. Transient failures
/// retry through the outbox backoff; a gone connection or a permanent refusal parks the message as
/// Failed and tells the organizer to delete it by hand. Idempotent: a 404 is success.</summary>
public sealed class CalendarMeetingCancelOutboxHandler(
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    ICalendarNotificationSender notifications) : IOutboxMessageHandler
{
    public string Type => OutboxMessageTypes.CalendarMeetingCancel;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarMeetingCancelPayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_meeting_cancel payload is empty.");
        var isZoom = payload.Provider == CalendarEventMeetingProviders.Zoom;
        var oauthProvider = isZoom ? "zoom" : "microsoft";

        var connection = await connections.GetByIdForTenantAsync(payload.TenantId, payload.ExternalCalendarConnectionId, ct);
        if (connection is null || connection.Status != ExternalCalendarConnectionStatuses.Active)
        {
            await ParkAsync(payload, "The organizer's meeting connection no longer exists.", ct);
            return;
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
        {
            await ParkAsync(payload, "The organizer's meeting connection needs to be reconnected.", ct);
            return;
        }

        try
        {
            await CancelAsync(isZoom, accessToken, payload.ExternalMeetingId, ct);
        }
        catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.Unauthorized)
        {
            var refreshed = await tokenProvider.ForceRefreshAccessTokenAsync(connection, oauthProvider, ct);
            if (refreshed is null)
            {
                await ParkAsync(payload, "The provider rejected the organizer's credentials.", ct);
                return;
            }
            try
            {
                await CancelAsync(isZoom, refreshed, payload.ExternalMeetingId, ct);
            }
            catch (MeetingProviderException again) when (again.Kind is MeetingProviderFailureKind.Unauthorized or MeetingProviderFailureKind.Permanent)
            {
                await ParkAsync(payload, again.Message, ct);
            }
        }
        catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.Permanent)
        {
            await ParkAsync(payload, ex.Message, ct);
        }
        // RateLimited / Ambiguous / ZoomMeetingInProgressException propagate: the outbox retries later.
    }

    private Task CancelAsync(bool isZoom, string accessToken, string externalMeetingId, CancellationToken ct)
        => isZoom
            ? zoomClient.CancelMeetingAsync(accessToken, externalMeetingId, ct)
            : teamsClient.CancelMeetingAsync(accessToken, externalMeetingId, ct);

    private async Task ParkAsync(CalendarMeetingCancelPayload payload, string reason, CancellationToken ct)
    {
        // Staged on the processor's context; it is saved with the message's Failed state.
        await notifications.NotifyOrganizerAboutMeetingAsync(
            payload.TenantId, payload.OrganizerUserId, CalendarMeetingNoticeCodes.CancelFailed, payload.EventTitle,
            CalendarMeetingProviderNames.Display(payload.Provider), ct);
        throw new OutboxPermanentFailureException(reason);
    }
}
