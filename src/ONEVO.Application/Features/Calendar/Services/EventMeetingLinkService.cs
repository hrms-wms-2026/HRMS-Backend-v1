using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
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
    IDateTimeProvider clock,
    IOutboxWriter outbox,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Provider call + outcome for a row this request has claimed. Runs to completion even if
    /// <paramref name="ct"/> is cancelled (a closed tab): an abandoned claim would strand the row in
    /// provisioning and the held-back invites unsent. The provider budget still bounds it.</summary>
    public async Task<Result<CreateEventMeetingResult>> ProvisionClaimedAsync(
        CalendarEvent evt, CalendarEventMeeting claimed, ExternalCalendarConnection connection, bool recovery,
        string organizerName, CancellationToken ct)
    {
        // The people invited to the event are added to the meeting too (plan C1 P2-3).
        var attendeeEmails = await AttendeeEmailsAsync(evt, CancellationToken.None);
        var outcome = await provisioner.ProvisionAsync(new MeetingProvisioningRequest(
            connection, claimed.Provider, evt.Id, claimed.LinkGeneration, evt.Title, evt.StartDate, evt.EndDate, recovery, attendeeEmails),
            CancellationToken.None);
        var providerName = CalendarMeetingProviderNames.Display(claimed.Provider);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // The provider call can take up to 90 s, so re-read the event's committed state instead of
            // trusting the copy loaded before it: it may have been edited or deleted in the meantime.
            var current = await events.GetByIdForTenantAsync(evt.TenantId, evt.Id, innerCt);
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
                if (current is null)
                {
                    // Deleted during the call: nobody gets a link to it, and the new remote meeting is
                    // cancelled after this commits (same keyed message the delete itself would queue).
                    claimed.Status = CalendarEventMeetingStatuses.Cancelled;
                    meetings.Update(claimed);
                    await outbox.EnqueueKeyedAsync(
                        OutboxMessageTypes.CalendarMeetingCancel,
                        new CalendarMeetingCancelPayload(
                            evt.TenantId, claimed.Provider, meeting.ExternalMeetingId, claimed.ExternalCalendarConnectionId,
                            evt.CreatedById, Guid.Empty, evt.Title),
                        $"meeting-cancel:{claimed.Id}:{claimed.LinkGeneration}", evt.TenantId, innerCt);
                    await unitOfWork.SaveChangesAsync(innerCt);
                    return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");
                }

                meetings.Update(claimed);
                // evt is tracked: setting the one property writes only meeting_link. A whole-row Update
                // here would overwrite edits committed during the provider call.
                evt.MeetingLink = meeting.JoinUrl;
                await SendLinkEmailsAsync(current, claimed, organizerName, innerCt);
                claimed.InvitesPending = false; // the link email doubles as the held-back invite
                await unitOfWork.SaveChangesAsync(innerCt);
                return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(meeting.JoinUrl));
            }

            var ambiguous = outcome.Kind == MeetingProvisioningOutcomeKind.Ambiguous;
            claimed.FailureReason = outcome.Reason;
            if (current is null)
            {
                // Deleted during the call: no invitations to a deleted event. An ambiguous call may have
                // left a meeting behind that only the organizer can find.
                claimed.Status = ambiguous ? CalendarEventMeetingStatuses.Unresolved : CalendarEventMeetingStatuses.Failed;
                claimed.ProvisioningStartedAt = null;
                meetings.Update(claimed);
                if (ambiguous)
                    await notifications.NotifyOrganizerAboutMeetingAsync(
                        evt.TenantId, evt.CreatedById, CalendarMeetingNoticeCodes.Unresolved, evt.Title, providerName, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");
            }

            claimed.Status = ambiguous ? CalendarEventMeetingStatuses.ProvisioningUnknown : CalendarEventMeetingStatuses.Failed;
            if (!ambiguous)
                claimed.ProvisioningStartedAt = null;
            // P1-5: nobody may be left uninvited because the link didn't happen.
            await SendPendingFallbackInvitesAsync(current, claimed, organizerName, innerCt);
            meetings.Update(claimed);
            await notifications.NotifyOrganizerAboutMeetingAsync(
                current.TenantId, current.CreatedById,
                ambiguous ? CalendarMeetingNoticeCodes.LinkUnknown : CalendarMeetingNoticeCodes.LinkFailed,
                current.Title, providerName, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);

            return outcome.Kind switch
            {
                MeetingProvisioningOutcomeKind.Ambiguous => Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown"),
                MeetingProvisioningOutcomeKind.NotConnected => Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected"),
                _ => Result<CreateEventMeetingResult>.Failure("meeting_link_failed", 502)
            };
        }, CancellationToken.None);
    }

    /// <summary>Invite emails WITHOUT a link to every participant and guest, keyed invite:{rowId} - the
    /// same key as the original invite, so anyone already invited is not invited again. Needs the
    /// caller's transaction (keyed emails).</summary>
    public async Task SendFallbackInvitesAsync(CalendarEvent evt, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        await notifications.SendMeetingLinkAsync(
            evt.TenantId, evt.Title, evt.StartDate, evt.Location,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Invite(p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Invite(g.Id))).ToList(),
            organizerName, meetingLink: null, Ics(evt), ct);
    }

    /// <summary>The P1-5 fallback, only while the event's invites are still held back for this link (development's
    /// InvitesHeldBack, persisted on the row as <see cref="CalendarEventMeeting.InvitesPending"/>): an event whose
    /// invites already went out never gets a second one. Clears the flag; the caller saves the row.</summary>
    public async Task SendPendingFallbackInvitesAsync(CalendarEvent evt, CalendarEventMeeting meeting, string organizerName, CancellationToken ct)
    {
        if (!meeting.InvitesPending)
            return;
        await SendFallbackInvitesAsync(evt, organizerName, ct);
        meeting.InvitesPending = false;
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

    /// <summary>A provisioning row whose request died (crash, lost save) becomes provisioning_unknown
    /// once stale - with the P1-5 fallback invites and a notice - so Retry and Remove can recover it.
    /// Returns the row as it now stands.</summary>
    public async Task<CalendarEventMeeting> RecoverStaleProvisioningAsync(
        CalendarEvent evt, CalendarEventMeeting snapshot, string organizerName, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (snapshot.Status != CalendarEventMeetingStatuses.Provisioning
            || !await meetings.TryMarkStaleProvisioningUnknownAsync(evt.TenantId, snapshot.Id, now - StaleAfter, now, ct))
            return snapshot;

        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var tracked = await meetings.GetTrackedByCalendarEventAsync(evt.TenantId, evt.Id, innerCt);
            if (tracked is not null)
            {
                await SendPendingFallbackInvitesAsync(evt, tracked, organizerName, innerCt);
                meetings.Update(tracked);
            }
            await NotifyOrganizerAsync(evt, CalendarMeetingNoticeCodes.LinkUnknown, snapshot.Provider, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);
        snapshot.Status = CalendarEventMeetingStatuses.ProvisioningUnknown;
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
        await notifications.SendMeetingLinkRemovedAsync(
            evt.TenantId, evt.Title, evt.StartDate, evt.Location,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.LinkRemoved(meeting.Id, meeting.LinkGeneration, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.LinkRemoved(meeting.Id, meeting.LinkGeneration, g.Id))).ToList(),
            organizerName, ct);
    }

    private async Task SendLinkEmailsAsync(CalendarEvent evt, CalendarEventMeeting meeting, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        await notifications.SendMeetingLinkAsync(
            evt.TenantId, evt.Title, evt.StartDate, evt.Location,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Link(meeting.Id, meeting.LinkGeneration, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Link(meeting.Id, meeting.LinkGeneration, g.Id))).ToList(),
            organizerName, meeting.JoinUrl, Ics(evt), ct);
    }

    /// <summary>The invite's .ics identity (plan C1 P2-1): stable UID per event, SEQUENCE from now.</summary>
    private CalendarEventIcs Ics(CalendarEvent evt) => CalendarEventIcs.For(evt.Id, evt.EndDate, evt.RecurrenceRule, clock.UtcNow);

    private async Task<IReadOnlyList<string>> AttendeeEmailsAsync(CalendarEvent evt, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        var emails = new List<string>(guests.Select(g => g.Email));
        foreach (var participant in participants)
        {
            if (await employees.GetByIdAsync(evt.TenantId, participant.EmployeeId, ct) is { Email: { Length: > 0 } email })
                emails.Add(email);
        }
        return emails;
    }

    private async Task<(IReadOnlyList<CalendarEventParticipant> Participants, IReadOnlyList<CalendarEventGuest> Guests)> AttendeesAsync(
        CalendarEvent evt, CancellationToken ct)
        => ((await events.GetParticipantsForEventsAsync(evt.TenantId, [evt.Id], ct)).GetValueOrDefault(evt.Id) ?? [],
            (await events.GetGuestsForEventsAsync(evt.TenantId, [evt.Id], ct)).GetValueOrDefault(evt.Id) ?? []);
}
