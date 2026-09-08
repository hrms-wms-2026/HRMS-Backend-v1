using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarSyncDirections
{
    public const string PullOnly = "pull_only";
    public const string PushOnly = "push_only";
    public const string TwoWay = "two_way";
    public const string Disabled = "disabled";
}

public static class ExternalCalendarConnectionStatuses
{
    public const string Active = "active";
    public const string ReauthRequired = "reauth_required";
    public const string Paused = "paused";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
}

public class ExternalCalendarConnection : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty; // CalendarExternalSources value: "google_calendar" | "outlook_calendar"
    public string ExternalAccountEmail { get; set; } = string.Empty;
    public string? ExternalCalendarId { get; set; }
    public string? ExternalCalendarName { get; set; }
    public byte[]? AccessTokenEncrypted { get; set; }
    public byte[] RefreshTokenEncrypted { get; set; } = [];
    public string ScopesJson { get; set; } = "[]";
    public string SyncDirection { get; set; } = CalendarSyncDirections.TwoWay;
    public string Status { get; set; } = ExternalCalendarConnectionStatuses.Active;
    public byte[]? SyncTokenEncrypted { get; set; }
    public byte[]? DeltaLinkEncrypted { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
