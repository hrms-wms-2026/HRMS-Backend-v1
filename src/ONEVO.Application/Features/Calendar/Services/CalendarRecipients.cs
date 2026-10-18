using System.Globalization;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>A participant to notify. When <see cref="EmailKey"/> is set the email is idempotent
/// (one per key, see <see cref="CalendarEmailKeys"/>) and must be sent inside the handler's
/// transaction; null sends an ordinary, unkeyed email.</summary>
public sealed record CalendarParticipantRecipient(Guid EmployeeId, string? EmailKey = null);

/// <summary>An external guest to email; <see cref="EmailKey"/> as for participants.</summary>
public sealed record CalendarGuestRecipient(string Email, string? EmailKey = null);

/// <summary>Email idempotency keys (spec §3.2.2 X-1). The recipient part is the participant or guest
/// row ID, so a retried operation can't email anyone twice while a genuinely new add, update or
/// cancellation still emails.</summary>
public static class CalendarEmailKeys
{
    public static string Invite(Guid rowId) => $"invite:{rowId}";

    /// <summary>Re-adding a removed participant revives the same row, so the previous removal's
    /// DeletedAt makes the re-invite a new email.</summary>
    public static string ReInvite(Guid participantRowId, DateTimeOffset previousDeletedAt) =>
        $"invite:{participantRowId}:{previousDeletedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}";

    public static string Update(Guid eventId, int revision, Guid rowId) =>
        $"update:{eventId}:{revision.ToString(CultureInfo.InvariantCulture)}:{rowId}";

    public static string Cancel(Guid eventId, DateTimeOffset? occurrenceStart, Guid rowId) =>
        $"cancel:{eventId}:{(occurrenceStart is { } start ? start.UtcTicks.ToString(CultureInfo.InvariantCulture) : "series")}:{rowId}";

    public static string Link(Guid meetingId, int generation, Guid rowId) =>
        $"link:{meetingId}:{generation.ToString(CultureInfo.InvariantCulture)}:{rowId}";

    public static string LinkRemoved(Guid meetingId, int generation, Guid rowId) =>
        $"link-removed:{meetingId}:{generation.ToString(CultureInfo.InvariantCulture)}:{rowId}";
}

/// <summary>In-app notices to an event's organizer about its online meeting (template codes).</summary>
public static class CalendarMeetingNoticeCodes
{
    public const string LinkFailed = "calendar_event_meeting_link_failed";
    public const string LinkUnknown = "calendar_event_meeting_link_unknown";
    public const string Unresolved = "calendar_event_meeting_unresolved";
    public const string CancelFailed = "calendar_event_meeting_cancel_failed";
}

public static class CalendarMeetingProviderNames
{
    public static string Display(string provider) =>
        provider == ONEVO.Domain.Features.Calendar.Entities.CalendarEventMeetingProviders.Zoom ? "Zoom" : "Microsoft Teams";
}
