namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;

/// <summary>
/// Payment gateway config list/detail response.
/// SECURITY: secrets are NEVER included. Active credential presence is surfaced as a bool.
/// </summary>
public sealed class PaymentGatewayConfigDto
{
    public Guid Id { get; init; }
    public string GatewayKey { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    /// <summary>Public key is NOT a secret and is safe to return.</summary>
    public string? PublicKey { get; init; }
    public string? MerchantId { get; init; }
    public string? WebhookUrl { get; init; }
    public bool IsActive { get; init; }
    /// <summary>True if an active (non-deactivated) credential version exists for this config.</summary>
    public bool HasActiveCredential { get; init; }
    /// <summary>Current credential version number for UI display; -1 when no credential exists.</summary>
    public int ActiveCredentialVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<PaymentGatewayCountryRouteDto> CountryRoutes { get; init; } = [];
}

/// <summary>Country route summary — no secret data.</summary>
public sealed class PaymentGatewayCountryRouteDto
{
    public Guid Id { get; init; }
    public string CountryCode { get; init; } = string.Empty;
    public string? CountryNameSnapshot { get; init; }
    public string Environment { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Resolved gateway response for tenant provisioning Step 3.
/// Returns just enough info to identify which gateway config serves a country.
/// </summary>
public sealed class ResolvedGatewayDto
{
    public Guid GatewayConfigId { get; init; }
    public string GatewayKey { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    /// <summary>False when no active gateway route exists for the requested country+environment.</summary>
    public bool IsResolved { get; init; }
}
