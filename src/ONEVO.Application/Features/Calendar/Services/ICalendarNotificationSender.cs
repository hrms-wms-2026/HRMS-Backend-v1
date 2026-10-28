namespace ONEVO.Application.Features.Calendar.Services;

public interface ICalendarNotificationSender
{
    /// <summary>In-app notification + invite email to each newly-added participant. Pass
    /// <paramref name="meetingLink"/> when the event already has an auto-generated Teams/Zoom
    /// join link at invite time so the email includes it. Pass <paramref name="sendInviteEmail"/>
    /// = false when the caller knows a Teams/Zoom meeting is about to be attached in the same
    /// one-click create flow (CreateCalendarEventCommand.PendingMeetingProvider) - the in-app
    /// notification still fires immediately, but the email is deferred to
    /// NotifyMeetingLinkAddedAsync so the recipient gets one invite (with the link) instead of two.</summary>
    Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default,
        string? meetingLink = null, bool sendInviteEmail = true, CalendarEventIcs? ics = null);

    /// <summary>Invite email (with the join link) to each existing participant, for when a
    /// Teams/Zoom meeting is added to an event after participants were already invited -
    /// the common case, since "Add Teams/Zoom meeting" is a separate action from inviting
    /// people. No in-app notification, matching NotifyParticipantsAddedAsync's email-only
    /// channel for this kind of update.</summary>
    Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default, CalendarEventIcs? ics = null);

    /// <summary>Keyed invite email (with the join link, or without one when <paramref name="meetingLink"/> is null -
    /// the P1-5 fallback) to each participant and guest, once per EmailKey, with the event's .ics.</summary>
    Task SendMeetingLinkAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, string? meetingLink, CalendarEventIcs? ics, CancellationToken ct = default);

    /// <summary>In-app to participants + keyed "link removed" email to participants and guests.</summary>
    Task SendMeetingLinkRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default);

    /// <summary>In-app notice to the organizer about their online meeting (see CalendarMeetingNoticeCodes).</summary>
    Task NotifyOrganizerAboutMeetingAsync(
        Guid tenantId, Guid organizerUserId, string templateCode, string eventTitle, string providerName, CancellationToken ct = default);

    /// <summary>Invite email (with the join link when there is one) to external guests identified only
    /// by email - covers both first invite and a meeting link added later.</summary>
    Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<string> guestEmails, string organizerName, string? meetingLink, CancellationToken ct = default, CalendarEventIcs? ics = null);

    /// <summary>In-app notification only (no email) to each participant that an event changed.</summary>
    Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only (no email) to each participant that an event was cancelled.</summary>
    Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant requested help resolving a conflict.</summary>
    Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant nominated a replacement.</summary>
    Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default);

    /// <summary>In-app notification to the organizer that a participant accepted or declined (plan C1 P1-2).</summary>
    Task NotifyRsvpAnsweredAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default);

    /// <summary>Email (with its .ics) to every participant and guest that an event was updated or cancelled, or to the people
    /// just removed (<see cref="CalendarEventEmailKinds"/>; plan C1 P1-3 / P1-4).</summary>
    Task EmailEventChangedAsync(
        Guid tenantId, string kind, CalendarEventIcs ics, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<Guid> employeeIds, IReadOnlyList<string> guestEmails, string organizerName, CancellationToken ct = default);

    /// <summary>Keyed version of <see cref="EmailEventChangedAsync"/>: one email per recipient key (see
    /// <see cref="CalendarEmailKeys"/>), with its .ics, so a retried change emails nobody twice. Needs the caller's
    /// transaction. Meetings-as-tasks Phase 2 sends every update, cancellation and removal this way.</summary>
    Task EmailEventChangedKeyedAsync(
        Guid tenantId, string kind, CalendarEventIcs ics, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests, string organizerName,
        CancellationToken ct = default);

    /// <summary>In-app notification to the nominee that they were nominated to replace a participant (P1-4).</summary>
    Task NotifyNomineeAsync(Guid tenantId, Guid nomineeUserId, string eventTitle, string responderName, CancellationToken ct = default);

    /// <summary>In-app notification to a participant the organizer removed from an event (P1-4).</summary>
    Task NotifyParticipantRemovedAsync(Guid tenantId, Guid employeeId, string eventTitle, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification to each participant that the meeting link was removed (P1-4).</summary>
    Task NotifyMeetingLinkRemovedAsync(Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app alert to the organizer that the Teams/Zoom link could not be created (P1-5).</summary>
    Task NotifyMeetingLinkFailedAsync(Guid tenantId, Guid organizerUserId, string eventTitle, string providerLabel, CancellationToken ct = default);

    /// <summary>In-app notification to the connection owner that their calendar integration needs attention (P1-6):
    /// <paramref name="problem"/> is reauth_required, failed or sync_conflict.</summary>
    Task NotifyConnectionProblemAsync(Guid tenantId, Guid userId, string providerLabel, string problem, CancellationToken ct = default);

    /// <summary>In-app reminder that an event starts soon (P2-2).</summary>
    Task NotifyEventReminderAsync(Guid tenantId, Guid userId, string eventTitle, DateTimeOffset startDate, CancellationToken ct = default);

    /// <summary>Queues a re-run of the meeting-exception rules for these employees after the current change commits (plan C2).</summary>
    Task QueueIncidentEvaluationAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
}
