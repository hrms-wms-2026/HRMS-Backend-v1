namespace ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

/// <summary>
/// Gateway metadata for Stripe, Paddle, PayHere.
/// Credential secrets live in payment_gateway_credentials (separate versioned rows).
/// Phase 1 canonical table: payment_gateway_configs.
/// </summary>
public class PaymentGatewayConfig
{
    public Guid Id { get; set; }

    /// <summary>Operator-set readable slug e.g. 'paddle-global-prod'. UNIQUE.</summary>
    public string GatewayKey { get; set; } = string.Empty;

    /// <summary>Provider discriminator: stripe | paddle | payhere</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>sandbox | production</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Friendly operator label e.g. "Paddle Global Production"</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Nullable gateway logo URL.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Nullable public identifier/key where applicable (e.g. Stripe publishable key).</summary>
    public string? PublicKey { get; set; }

    /// <summary>Nullable: Paddle seller ID or PayHere merchant ID.</summary>
    public string? MerchantId { get; set; }

    /// <summary>Gateway callback/notify URL for incoming webhooks.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Whether this config may be used for payment collection.</summary>
    public bool IsActive { get; set; }

    /// <summary>Platform user who created this config. FK -> platform_users.</summary>
    public Guid? CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<PaymentGatewayCredential> Credentials { get; set; } = new List<PaymentGatewayCredential>();
    public ICollection<PaymentGatewayCountryRoute> CountryRoutes { get; set; } = new List<PaymentGatewayCountryRoute>();
}
