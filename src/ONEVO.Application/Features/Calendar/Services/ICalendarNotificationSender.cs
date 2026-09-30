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
        string? meetingLink = null, bool sendInviteEmail = true);

    /// <summary>Invite email (with the join link) to each existing participant, for when a
    /// Teams/Zoom meeting is added to an event after participants were already invited -
    /// the common case, since "Add Teams/Zoom meeting" is a separate action from inviting
    /// people. No in-app notification, matching NotifyParticipantsAddedAsync's email-only
    /// channel for this kind of update.</summary>
    Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default);

    /// <summary>Invite email (with the join link when there is one) to external guests identified only
    /// by email - covers both first invite and a meeting link added later.</summary>
    Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<string> guestEmails, string organizerName, string? meetingLink, CancellationToken ct = default);

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
}
