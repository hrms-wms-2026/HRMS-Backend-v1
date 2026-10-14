using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Everything that happens after a meeting row is claimed: the provider call, the row's next
/// state, and who gets told what (spec §3.2.2, P1-5). Stateless; scoped like its dependencies.</summary>
public sealed class EventMeetingLinkService(
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    IExternalCalendarConnectionRepository connections,
    IMeetingProvisioner provisioner,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public async Task<Result<CreateEventMeetingResult>> ProvisionClaimedAsync(
        CalendarEvent evt, CalendarEventMeeting claimed, ExternalCalendarConnection connection, bool recovery,
        string organizerName, CancellationToken ct)
    {
        var outcome = await provisioner.ProvisionAsync(new MeetingProvisioningRequest(
            connection, claimed.Provider, evt.Id, claimed.LinkGeneration, evt.Title, evt.StartDate, evt.EndDate, recovery), ct);
        var providerName = CalendarMeetingProviderNames.Display(claimed.Provider);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            claimed.UpdatedAt = clock.UtcNow;
            if (outcome.Kind == MeetingProvisioningOutcomeKind.Created)
            {
                var meeting = outcome.Meeting!;
                claimed.Status = CalendarEventMeetingStatuses.Active;
                claimed.ExternalMeetingId = meeting.ExternalMeetingId;
                claimed.JoinUrl = meeting.JoinUrl;
                claimed.OrganizerJoinUrl = meeting.OrganizerJoinUrl;
                claimed.PasscodeOrPin = meeting.PasscodeOrPin;
                claimed.FailureReason = null;
                claimed.ProvisioningStartedAt = null;
                meetings.Update(claimed);
                evt.MeetingLink = meeting.JoinUrl;
                events.Update(evt);
                await SendLinkEmailsAsync(evt, claimed, organizerName, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(meeting.JoinUrl));
            }

            var ambiguous = outcome.Kind == MeetingProvisioningOutcomeKind.Ambiguous;
            claimed.Status = ambiguous ? CalendarEventMeetingStatuses.ProvisioningUnknown : CalendarEventMeetingStatuses.Failed;
            claimed.FailureReason = outcome.Reason;
            if (!ambiguous)
                claimed.ProvisioningStartedAt = null;
            meetings.Update(claimed);
            // P1-5: nobody may be left uninvited because the link didn't happen.
            await SendFallbackInvitesAsync(evt, organizerName, innerCt);
            await notifications.NotifyOrganizerAboutMeetingAsync(
                evt.TenantId, evt.CreatedById,
                ambiguous ? CalendarMeetingNoticeCodes.LinkUnknown : CalendarMeetingNoticeCodes.LinkFailed,
                evt.Title, providerName, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);

            return outcome.Kind switch
            {
                MeetingProvisioningOutcomeKind.Ambiguous => Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown"),
                MeetingProvisioningOutcomeKind.NotConnected => Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected"),
                _ => Result<CreateEventMeetingResult>.Failure("meeting_link_failed", 502)
            };
        }, ct);
    }

    /// <summary>Invite emails WITHOUT a link to every participant and guest, keyed invite:{rowId} - the
    /// same key as the original invite, so anyone already invited is not invited again. Needs the
    /// caller's transaction (keyed emails).</summary>
    public async Task SendFallbackInvitesAsync(CalendarEvent evt, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count > 0)
            await notifications.NotifyMeetingLinkAddedAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Invite(p.Id))).ToList(),
                organizerName, meetingLink: null, ct);
        if (guests.Count > 0)
            await notifications.NotifyGuestsAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Invite(g.Id))).ToList(),
                organizerName, meetingLink: null, ct);
    }

    /// <summary>In-app notice to the event's organizer (see CalendarMeetingNoticeCodes).</summary>
    public Task NotifyOrganizerAsync(CalendarEvent evt, string noticeCode, string provider, CancellationToken ct)
        => notifications.NotifyOrganizerAboutMeetingAsync(
            evt.TenantId, evt.CreatedById, noticeCode, evt.Title, CalendarMeetingProviderNames.Display(provider), ct);

    /// <summary>Spec: an event that ended while provisioning_unknown becomes unresolved when next read
    /// (the stray-meeting data is kept and the organizer is warned). Returns the row as it now stands.</summary>
    public async Task<CalendarEventMeeting> ExpireIfEndedAsync(CalendarEvent evt, CalendarEventMeeting snapshot, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (snapshot.Status != CalendarEventMeetingStatuses.ProvisioningUnknown || evt.EndDate >= now)
            return snapshot;
        if (!await meetings.TryMarkUnknownUnresolvedAsync(evt.TenantId, snapshot.Id, now, ct))
            return await meetings.GetByCalendarEventAsync(evt.TenantId, evt.Id, ct) ?? snapshot;

        await notifications.NotifyOrganizerAboutMeetingAsync(
            evt.TenantId, evt.CreatedById, CalendarMeetingNoticeCodes.Unresolved, evt.Title,
            CalendarMeetingProviderNames.Display(snapshot.Provider), ct);
        await unitOfWork.SaveChangesAsync(ct);
        snapshot.Status = CalendarEventMeetingStatuses.Unresolved;
        return snapshot;
    }

    /// <summary>Remove on provisioning_unknown: resolve the remote side first (spec §3.2.2).</summary>
    public async Task<Result> ResolveUnknownForRemovalAsync(CalendarEvent evt, CalendarEventMeeting snapshot, CancellationToken ct)
    {
        if (!await meetings.TryClaimUnknownAsync(evt.TenantId, snapshot.Id, clock.UtcNow, ct))
            return Result.Conflict("meeting_link_in_progress");

        var meeting = (await meetings.GetTrackedByCalendarEventAsync(evt.TenantId, evt.Id, ct))!;
        var connection = await connections.GetByIdForTenantAsync(evt.TenantId, meeting.ExternalCalendarConnectionId, ct);
        var resolved = connection is not null && await provisioner.ResolveForRemovalAsync(new MeetingProvisioningRequest(
            connection, meeting.Provider, evt.Id, meeting.LinkGeneration, evt.Title, evt.StartDate, evt.EndDate, Recovery: true), ct);

        meeting.Status = resolved ? CalendarEventMeetingStatuses.Cancelled : CalendarEventMeetingStatuses.Unresolved;
        meeting.ProvisioningStartedAt = null;
        meeting.UpdatedAt = clock.UtcNow;
        meetings.Update(meeting);
        if (!resolved)
            await notifications.NotifyOrganizerAboutMeetingAsync(
                evt.TenantId, evt.CreatedById, CalendarMeetingNoticeCodes.Unresolved, evt.Title,
                CalendarMeetingProviderNames.Display(meeting.Provider), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return resolved ? Result.Success() : Result.Conflict("meeting_link_unresolved");
    }

    /// <summary>P1-4: the link went away - in-app to participants, email to participants and guests,
    /// once per generation. Needs the caller's transaction.</summary>
    public async Task SendLinkRemovedAsync(CalendarEvent evt, CalendarEventMeeting meeting, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        await notifications.NotifyMeetingLinkRemovedAsync(
            evt.TenantId, evt.Title, evt.StartDate, evt.Location,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.LinkRemoved(meeting.Id, meeting.LinkGeneration, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.LinkRemoved(meeting.Id, meeting.LinkGeneration, g.Id))).ToList(),
            organizerName, ct);
    }

    private async Task SendLinkEmailsAsync(CalendarEvent evt, CalendarEventMeeting meeting, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count > 0)
            await notifications.NotifyMeetingLinkAddedAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Link(meeting.Id, meeting.LinkGeneration, p.Id))).ToList(),
                organizerName, meeting.JoinUrl, ct);
        if (guests.Count > 0)
            await notifications.NotifyGuestsAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Link(meeting.Id, meeting.LinkGeneration, g.Id))).ToList(),
                organizerName, meeting.JoinUrl, ct);
    }

    private async Task<(IReadOnlyList<CalendarEventParticipant> Participants, IReadOnlyList<CalendarEventGuest> Guests)> AttendeesAsync(
        CalendarEvent evt, CancellationToken ct)
        => ((await events.GetParticipantsForEventsAsync(evt.TenantId, [evt.Id], ct)).GetValueOrDefault(evt.Id) ?? [],
            (await events.GetGuestsForEventsAsync(evt.TenantId, [evt.Id], ct)).GetValueOrDefault(evt.Id) ?? []);
}
