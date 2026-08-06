namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Pending invitation for a platform manager (canonical `platform_user_invites`).
/// Used only for Developer Platform user invitation, never tenant employee onboarding.
/// The raw invite token is never stored; only its SHA-256 hash.
/// </summary>
public class PlatformUserInvite
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string InviteTokenHash { get; set; } = string.Empty;
    public Guid InvitedById { get; set; }
    public Guid? PlatformUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
