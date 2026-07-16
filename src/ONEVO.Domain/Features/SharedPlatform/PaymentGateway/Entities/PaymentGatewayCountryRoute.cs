namespace ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

/// <summary>
/// Country-to-gateway routing for subscription and invoice collection.
/// Enforces "one active route per country_code + environment" constraint.
/// Phase 1 canonical table: payment_gateway_country_routes.
/// </summary>
public class PaymentGatewayCountryRoute
{
    public Guid Id { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code e.g. "LK", "GB", "US".</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Display snapshot of the country name at time of route creation.</summary>
    public string? CountryNameSnapshot { get; set; }

    /// <summary>FK -> payment_gateway_configs.</summary>
    public Guid GatewayConfigId { get; set; }

    /// <summary>sandbox | production</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Whether this route can be used for payment resolution.</summary>
    public bool IsActive { get; set; }

    /// <summary>Platform user who created this route. FK -> platform_users.</summary>
    public Guid? CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public PaymentGatewayConfig? GatewayConfig { get; set; }
}
