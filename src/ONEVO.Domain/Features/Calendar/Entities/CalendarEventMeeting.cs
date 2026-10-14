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

/// <summary>Outcome of attendance sync (spec §3.2.1). Null = not decided yet (still retried).
/// <see cref="Unavailable"/> means "no data received", never "nobody attended".</summary>
public static class CalendarAttendanceSyncStatuses
{
    public const string Synced = "synced";
    public const string Unavailable = "attendance_unavailable";
    public const string Failed = "sync_failed";
}

public class CalendarEventMeeting : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid ExternalCalendarConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalMeetingId { get; set; } = string.Empty;
    public string JoinUrl { get; set; } = string.Empty;
    public string? OrganizerJoinUrl { get; set; }
    public string? PasscodeOrPin { get; set; }
    public string Status { get; set; } = CalendarEventMeetingStatuses.Active;
    public DateTimeOffset? LastAttendanceSyncedAt { get; set; }
    public string? AttendanceSyncStatus { get; set; }

    /// <summary>Starts at 1; +1 for each genuinely new link. Retries of the same create reuse it, so the
    /// provider-side identity (CalendarMeetingCorrelation) and the link email keys stay the same.</summary>
    public int LinkGeneration { get; set; } = 1;
    public DateTimeOffset? ProvisioningStartedAt { get; set; }
    /// <summary>Why the last attempt didn't produce a link (auth_failed, rate_limited, rejected,
    /// not_connected, timeout_or_server_error, correlation_unavailable).</summary>
    public string? FailureReason { get; set; }
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
