namespace ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

/// <summary>
/// Encrypted payment credentials versioned per gateway config.
/// New secret = new active row; prior rows are deactivated.
/// Phase 1 canonical table: payment_gateway_credentials.
/// SECURITY: secret_encrypted and webhook_secret_encrypted are bytea (IEncryptionService.EncryptBytes).
/// These values are NEVER returned by any API GET response.
/// </summary>
public class PaymentGatewayCredential
{
    public Guid Id { get; set; }

    /// <summary>FK -> payment_gateway_configs.</summary>
    public Guid PaymentGatewayConfigId { get; set; }

    /// <summary>
    /// AES-256 encrypted Stripe secret key, Paddle API key, or PayHere merchant secret.
    /// NEVER returned by API. Stored as bytea via IEncryptionService.EncryptBytes().
    /// </summary>
    public byte[] SecretEncrypted { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// AES-256 encrypted webhook signing secret when separate from the main credential.
    /// Nullable. NEVER returned by API.
    /// </summary>
    public byte[]? WebhookSecretEncrypted { get; set; }

    /// <summary>Key version identifier used by IEncryptionService for rotation support.</summary>
    public string EncryptionKeyVersion { get; set; } = string.Empty;

    /// <summary>Monotonic version counter per gateway config (1, 2, 3...).</summary>
    public int CredentialVersion { get; set; }

    /// <summary>Only the active row may be used for provider calls.</summary>
    public bool IsActive { get; set; }

    /// <summary>Platform user who added/rotated this credential. FK -> platform_users.</summary>
    public Guid RotatedById { get; set; }

    public DateTimeOffset RotatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Platform user who deactivated this credential. Nullable.</summary>
    public Guid? DeactivatedById { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    // Navigation
    public PaymentGatewayConfig? GatewayConfig { get; set; }
}
