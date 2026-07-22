using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Short-lived, server-side MFA login challenge state created after password verification and
/// before MFA verification completes. Stores only the SHA-256 hash of the opaque challenge token;
/// the raw value exists only in the browser's HttpOnly onevo_mfa cookie and is never persisted.
/// </summary>
public class MfaChallenge : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string ChallengeHash { get; set; } = string.Empty;
    public int FailedAttemptCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
