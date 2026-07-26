using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Durable server-side authority for the pre-session Legal &amp; Privacy completion flow. The
/// browser holds only the raw opaque handle in the HttpOnly onevo_legal_pending cookie; the
/// database stores its SHA-256 hash. Bound to the verified tenant/user from the completed
/// login/MFA/Google-SSO attempt that produced it; authorizes only pending-document retrieval and
/// login-completion acceptance, never a normal session by itself.
/// </summary>
public class LegalLoginChallenge : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string ChallengeHash { get; set; } = string.Empty;
    public string CsrfTokenHash { get; set; } = string.Empty;

    /// <summary>One of: password, mfa, google_sso, stale_session.</summary>
    public string Origin { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public Guid? SupersededById { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
