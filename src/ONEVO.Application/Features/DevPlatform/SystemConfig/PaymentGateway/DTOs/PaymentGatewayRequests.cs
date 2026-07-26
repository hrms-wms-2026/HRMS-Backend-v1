using System.ComponentModel.DataAnnotations;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;

/// <summary>
/// Request to create a payment gateway config with initial credentials and country routes.
/// Gateway credentials are accepted here (HTTPS POST body only) and immediately encrypted;
/// they are never stored in plaintext and never returned by any GET response.
/// </summary>
public sealed class CreatePaymentGatewayRequest
{
    [Required, MaxLength(80)]
    public string GatewayKey { get; init; } = string.Empty;

    [Required, MaxLength(30)]
    public string Provider { get; init; } = string.Empty;   // stripe | paddle | payhere

    [Required, MaxLength(20)]
    public string Environment { get; init; } = string.Empty; // sandbox | production

    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? LogoUrl { get; init; }

    [MaxLength(255)]
    public string? PublicKey { get; init; }

    [MaxLength(100)]
    public string? MerchantId { get; init; }

    [MaxLength(500)]
    public string? WebhookUrl { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Provider secret (Stripe secret key, Paddle API key, PayHere merchant secret).
    /// Required at create time. Encrypted with IEncryptionService before persistence.
    /// NEVER returned by any GET response.
    /// </summary>
    [Required]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Provider webhook signing secret - separate from the main secret for providers that use one.
    /// Encrypted with IEncryptionService before persistence. NEVER returned.
    /// </summary>
    public string? WebhookSecret { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country codes to assign to this gateway.</summary>
    public IReadOnlyList<string> CountryCodes { get; init; } = [];

    /// <summary>Country name snapshots parallel to CountryCodes for display/audit.</summary>
    public IReadOnlyList<string?> CountryNameSnapshots { get; init; } = [];
}

/// <summary>
/// Request to rotate (replace) credentials for an existing gateway config.
/// The new secret is encrypted and stored as a new version row; prior active row is deactivated.
/// </summary>
public sealed class RotatePaymentGatewayCredentialRequest
{
    /// <summary>New provider secret. Encrypted before persistence. NEVER returned.</summary>
    [Required]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>New webhook secret if changing. Nullable. Encrypted before persistence.</summary>
    public string? WebhookSecret { get; init; }
}

/// <summary>
/// Request to update non-credential payment gateway metadata (display name, webhook URL, active status, country routes).
/// Does NOT accept secret fields - use RotatePaymentGatewayCredentialRequest to change secrets.
/// </summary>
public sealed class UpdatePaymentGatewayMetadataRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; init; }

    [MaxLength(500)]
    public string? LogoUrl { get; init; }

    [MaxLength(255)]
    public string? PublicKey { get; init; }

    [MaxLength(100)]
    public string? MerchantId { get; init; }

    [MaxLength(500)]
    public string? WebhookUrl { get; init; }

    public bool? IsActive { get; init; }

    /// <summary>
    /// Replacement country code list for this gateway.
    /// When supplied: existing routes for codes NOT in this list are deactivated;
    /// new codes are added as active routes.
    /// </summary>
    public IReadOnlyList<string>? CountryCodes { get; init; }

    public IReadOnlyList<string?>? CountryNameSnapshots { get; init; }
}

/// <summary>
/// Credentials for live account verification before save.
/// Accepted at the verify endpoint only - NOT persisted.
/// </summary>
public sealed class VerifyGatewayCredentialsRequest
{
    [Required, MaxLength(30)]
    public string Provider { get; init; } = string.Empty;

    /// <summary>Credentials to verify. Not persisted.</summary>
    [Required]
    public Dictionary<string, string> Credentials { get; init; } = new();
}

/// <summary>Response from gateway account verification.</summary>
public sealed class GatewayVerificationResult
{
    public bool IsVerified { get; init; }
    public string? AccountName { get; init; }
    public string? Country { get; init; }
    public string? DefaultCurrency { get; init; }
    public IReadOnlyList<string> EnabledPaymentMethods { get; init; } = [];
    public bool? ChargesEnabled { get; init; }
    public bool? PayoutsEnabled { get; init; }
    public string? ErrorMessage { get; init; }
}
