namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Server-side cookie-backed session for a Developer Platform user (canonical
/// `platform_user_sessions`, replacing the noncanonical `platform_admin_sessions`).
/// The browser holds only the protected cookie; the database stores only the
/// SHA-256 hash of the opaque session key and the hash of the session-bound CSRF token.
/// LastActivityAt/RevokedAt/CsrfTokenHash/UserAgent extend the canonical column list
/// per the architecture document's session-tracking and CSRF-binding requirements.
/// </summary>
public class PlatformUserSession
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string CsrfTokenHash { get; set; } = string.Empty;
}
