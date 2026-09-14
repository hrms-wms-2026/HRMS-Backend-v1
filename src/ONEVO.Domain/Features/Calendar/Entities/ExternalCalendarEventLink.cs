using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class ExternalCalendarSyncStatuses
{
    public const string Synced = "synced";
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Conflict = "conflict";
}

public static class ExternalCalendarLinkDirections
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
}

public class ExternalCalendarEventLink : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid ExternalCalendarConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalCalendarId { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string? ExternalEtag { get; set; }
    public string SyncDirection { get; set; } = ExternalCalendarLinkDirections.Inbound;
    public string SyncStatus { get; set; } = ExternalCalendarSyncStatuses.Pending;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
