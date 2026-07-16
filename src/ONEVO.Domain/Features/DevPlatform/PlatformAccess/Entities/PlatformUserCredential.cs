namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Database-backed credential lifecycle for a Developer Platform user.
/// Sensitive hashes must never be serialized into API responses or logs.
/// </summary>
public sealed class PlatformUserCredential
{
    public const string PasswordType = "password";
    public const string BCryptAlgorithm = "bcrypt-12";

    public Guid Id { get; set; }
    public Guid PlatformUserId { get; set; }
    public string CredentialType { get; set; } = PasswordType;
    public string? PasswordHash { get; set; }
    public string? PasswordAlgorithm { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? ResetTokenHash { get; set; }
    public DateTimeOffset? ResetTokenExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
