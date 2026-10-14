namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Stages calendar notifications in the outbox. Unkeyed rows are only persisted by the
/// caller's SaveChangesAsync, so call these BEFORE it (spec §3.1). Keyed emails (a recipient with an
/// EmailKey) are inserted immediately and need the caller's open transaction.</summary>
public interface ICalendarNotificationSender
{
    /// <summary>In-app notification + invite email to each newly-added participant. Pass
    /// <paramref name="sendInviteEmail"/> = false when a Teams/Zoom meeting is about to be attached
    /// in the same create flow; the in-app notification still fires.</summary>
    Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, CancellationToken ct = default,
        string? meetingLink = null, bool sendInviteEmail = true);

    /// <summary>Invite email (with the join link) to each existing participant when a Teams/Zoom
    /// meeting is added after they were invited. No in-app notification.</summary>
    Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default);

    /// <summary>Invite email to external guests (no account, so no in-app notification).</summary>
    Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, string? meetingLink, CancellationToken ct = default);

    /// <summary>In-app to participants + "updated" email to participants and guests.</summary>
    Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default);

    /// <summary>In-app to participants + "cancelled" email to participants and guests.
    /// <paramref name="startDate"/> is the cancelled occurrence's start (or the event's).</summary>
    Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant requested help resolving a conflict.</summary>
    Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant nominated a replacement.</summary>
    Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant accepted or declined.</summary>
    Task NotifyRsvpAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default);

    /// <summary>In-app notification only, to the nominated replacement.</summary>
    Task NotifyNomineeAsync(
        Guid tenantId, Guid nomineeEmployeeId, string eventTitle, string responderName, CancellationToken ct = default);

    /// <summary>In-app + "removed" email to a participant the organizer removed.</summary>
    Task NotifyParticipantRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, Guid employeeId,
        string organizerName, CancellationToken ct = default);

    /// <summary>"Removed" email to a guest the organizer removed.</summary>
    Task NotifyGuestRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string guestEmail,
        string organizerName, CancellationToken ct = default);
}
