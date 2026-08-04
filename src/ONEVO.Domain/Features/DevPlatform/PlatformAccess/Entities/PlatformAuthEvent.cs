namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Immutable authentication/access history row for Developer Platform users
/// (canonical `platform_auth_events`). MetadataJson must contain safe structured
/// context only — never passwords, raw tokens, raw CSRF values, or secrets.
/// </summary>
public class PlatformAuthEvent
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string SessionRevoked = "session_revoked";
    public const string LogoutSucceeded = "logout_succeeded";
    public const string AdminPasswordResetRequested = "admin_password_reset_requested";
    public const string AdminPasswordResetCompleted = "admin_password_reset_completed";

    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? UserAgent { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
