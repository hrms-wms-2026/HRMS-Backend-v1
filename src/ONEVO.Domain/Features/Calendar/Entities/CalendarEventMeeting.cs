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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
