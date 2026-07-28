namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Administrative user of the Developer Platform (canonical `platform_users`).
/// Not tenant-scoped; effective access is resolved through
/// platform_user_roles -> platform_role_permissions -> platform_permissions.
/// </summary>
public class PlatformUser
{
    public const string StatusPending = "pending";
    public const string StatusActive = "active";
    public const string StatusInactive = "inactive";

    public const string MfaNotEnrolled = "not_enrolled";
    public const string MfaEnrolled = "enrolled";
    public const string MfaLocked = "locked";

    public const string InvitePending = "pending";
    public const string InviteAccepted = "accepted";
    public const string InviteRevoked = "revoked";
    public const string InviteExpired = "expired";

    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? GoogleSub { get; set; }
    public string Status { get; set; } = StatusPending;
    public string MfaStatus { get; set; } = MfaNotEnrolled;
    public string? MfaSecret { get; set; }
    public string InviteStatus { get; set; } = InvitePending;
    public Guid? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}
