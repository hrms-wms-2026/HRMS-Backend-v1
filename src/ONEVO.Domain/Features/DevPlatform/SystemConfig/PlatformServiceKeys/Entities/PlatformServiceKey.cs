namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;

/// <summary>
/// ONEVO-owned third-party service API key used internally across all tenants.
/// Examples: Resend/SendGrid for platform transactional email, Cloudflare DNS/WAF, Cloudflare R2.
/// This is global platform credential storage — NOT tenant-level notification provider
/// configuration (that is notification_channels, a separate concern).
/// Phase 1 canonical table: platform_service_keys.
/// SECURITY: api_key_encrypted is AES-256 encrypted text (IEncryptionService.Encrypt).
/// It is NEVER returned by any API GET response.
/// </summary>
public class PlatformServiceKey
{
    public Guid Id { get; set; }

    /// <summary>Stable slug identifying the service: resend, sendgrid, cloudflare, cloudflare_r2.</summary>
    public string ServiceKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// AES-256 encrypted API key. NEVER returned by API.
    /// Stored as text via IEncryptionService.Encrypt(); decrypted only in memory
    /// by server-side callers (verification service, runtime resolver).
    /// </summary>
    public string ApiKeyEncrypted { get; set; } = string.Empty;

    /// <summary>Only active keys may be resolved for runtime use.</summary>
    public bool IsActive { get; set; }

    /// <summary>When the key was last successfully verified. Nullable.</summary>
    public DateTimeOffset? LastVerifiedAt { get; set; }

    /// <summary>Platform user who last created/updated/rotated this key. FK -> platform_users.</summary>
    public Guid UpdatedById { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
