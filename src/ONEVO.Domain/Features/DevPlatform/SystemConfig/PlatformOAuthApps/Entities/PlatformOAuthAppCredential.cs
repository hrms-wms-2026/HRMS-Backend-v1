namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

/// <summary>
/// Encrypted, versioned secret material for an ONEVO OAuth app registration.
/// Secret rotation creates a new active row; prior rows are deactivated, never overwritten.
/// Phase 1 canonical table: platform_oauth_app_credentials.
/// SECURITY: client_secret_encrypted / private_key_encrypted are AES-256 encrypted text
/// (IEncryptionService.Encrypt). They are NEVER returned by any API response.
/// </summary>
public class PlatformOAuthAppCredential
{
    public Guid Id { get; set; }

    /// <summary>FK -> platform_oauth_apps.</summary>
    public Guid PlatformOAuthAppId { get; set; }

    /// <summary>
    /// AES-256 encrypted OAuth client secret. NEVER returned by API.
    /// Decrypted only in memory by server-side callers (future connect-flow resolver).
    /// </summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    /// <summary>
    /// Nullable AES-256 encrypted GitHub App private key or provider-specific secret material.
    /// NEVER returned by API.
    /// </summary>
    public string? PrivateKeyEncrypted { get; set; }

    /// <summary>Key version identifier used by IEncryptionService for rotation support.</summary>
    public string EncryptionKeyVersion { get; set; } = string.Empty;

    /// <summary>Monotonic version counter per OAuth app (1, 2, 3…).</summary>
    public int CredentialVersion { get; set; }

    /// <summary>Only one active credential row per platform_oauth_app_id.</summary>
    public bool IsActive { get; set; }

    /// <summary>Platform user who added/rotated this credential. FK -> platform_users.</summary>
    public Guid RotatedById { get; set; }

    public DateTimeOffset RotatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Platform user who deactivated this credential. Nullable FK -> platform_users.</summary>
    public Guid? DeactivatedById { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }
}
