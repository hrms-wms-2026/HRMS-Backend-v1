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
}

public static class CalendarAttendanceSyncErrors
{
    public const string ProviderError = "provider_error";
    public const string NotConnected = "not_connected";
    public const string ReauthRequired = "reauth_required";
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
    /// <summary>Set when the provider still had no attendance report
    /// 24 h after the meeting ended (plan C0 P0-2): the meeting stops being retried and shows "attendance unavailable".</summary>
    public DateTimeOffset? AttendanceUnavailableAt { get; set; }

    /// <summary>Set when attendance still could not be fetched (provider error, no connection, sign-in
    /// needed) at the cutoff: the meeting stops being retried. Unlike <see cref="AttendanceUnavailableAt"/>
    /// the provider never answered, so this means "unknown", never "nobody attended".</summary>
    public DateTimeOffset? AttendanceSyncFailedAt { get; set; }

    /// <summary>Why the sync failed: <see cref="CalendarAttendanceSyncErrors"/>.</summary>
    public string? AttendanceSyncError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
