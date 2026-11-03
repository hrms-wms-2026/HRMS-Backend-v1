using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventMeetingProviders
{
    public const string MicrosoftTeams = "microsoft_teams";
    public const string Zoom = "zoom";
}

public static class CalendarEventMeetingStatuses
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    /// <summary>A request owns the row and is calling the provider (claimed atomically).</summary>
    public const string Provisioning = "provisioning";
    /// <summary>The provider call was ambiguous (timeout/5xx) or its request vanished: the meeting may exist remotely.</summary>
    public const string ProvisioningUnknown = "provisioning_unknown";
    /// <summary>A meeting may still exist remotely and couldn't be found; only the organizer's confirmation clears it.</summary>
    public const string Unresolved = "unresolved";
}

public static class CalendarAttendanceSyncErrors
{
    public const string ProviderError = "provider_error";
    public const string NotConnected = "not_connected";
    public const string ReauthRequired = "reauth_required";
}

/// <summary>Who owns a provider meeting link (meetings-as-tasks Phase 2 spec 4.1).</summary>
public static class CalendarMeetingOwnerKinds
{
    /// <summary>A calendar event; <see cref="CalendarEventMeeting.CalendarEventId"/> is set.</summary>
    public const string CalendarEvent = "calendar_event";
    /// <summary>A meeting series, through meeting_series_links; <see cref="CalendarEventMeeting.CalendarEventId"/> is null.</summary>
    public const string MeetingSeries = "meeting_series";
}

public class CalendarEventMeeting : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Set exactly when <see cref="OwnerKind"/> is <see cref="CalendarMeetingOwnerKinds.CalendarEvent"/>.</summary>
    public Guid? CalendarEventId { get; set; }
    public string OwnerKind { get; set; } = CalendarMeetingOwnerKinds.CalendarEvent;
    public Guid ExternalCalendarConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalMeetingId { get; set; } = string.Empty;
    public string JoinUrl { get; set; } = string.Empty;
    public string? OrganizerJoinUrl { get; set; }
    public string? PasscodeOrPin { get; set; }
    public string Status { get; set; } = CalendarEventMeetingStatuses.Active;
    public DateTimeOffset? LastAttendanceSyncedAt { get; set; }
    /// <summary>Set when the provider still had no attendance report
    /// 24 h after the meeting ended (plan C0 P0-2): the meeting stops being retried and shows "attendance unavailable".</summary>
    public DateTimeOffset? AttendanceUnavailableAt { get; set; }

    /// <summary>Set when attendance still could not be fetched (provider error, no connection, sign-in
    /// needed) at the cutoff: the meeting stops being retried. Unlike <see cref="AttendanceUnavailableAt"/>
    /// the provider never answered, so this means "unknown", never "nobody attended".</summary>
    public DateTimeOffset? AttendanceSyncFailedAt { get; set; }

    /// <summary>Why the sync failed: <see cref="CalendarAttendanceSyncErrors"/>.</summary>
    public string? AttendanceSyncError { get; set; }

    /// <summary>Starts at 1; +1 for each genuinely new link. Retries of the same create reuse it, so the
    /// provider-side identity (CalendarMeetingCorrelation) and the link email keys stay the same.</summary>
    public int LinkGeneration { get; set; } = 1;
    public DateTimeOffset? ProvisioningStartedAt { get; set; }
    /// <summary>Why the last attempt didn't produce a link (auth_failed, rate_limited, rejected,
    /// not_connected, timeout_or_server_error, correlation_unavailable).</summary>
    public string? FailureReason { get; set; }

    /// <summary>The event's invites are held back for this link (development's P1-5 InvitesHeldBack): the link email
    /// doubles as the invite, and a failed link sends the invites without one - once - then clears this.</summary>
    public bool InvitesPending { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Provider-side identities for one link generation of an event (spec §3.2.2). The same
/// (event, generation) always maps to the same remote meeting, so a retry adopts it instead of
/// creating a second one.</summary>
public static class CalendarMeetingCorrelation
{
    public static string TeamsExternalId(Guid eventId, int generation) => $"{eventId}:{generation}";
    public static string ZoomToken(Guid eventId, int generation) => $"onexso:{eventId}:{generation}";
}
