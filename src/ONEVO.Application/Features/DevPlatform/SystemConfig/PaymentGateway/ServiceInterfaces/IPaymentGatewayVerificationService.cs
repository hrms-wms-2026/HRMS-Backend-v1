using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;

/// <summary>
/// Verifies payment gateway credentials against the live provider API before save.
/// This service does NOT persist credentials - it is called at the verify endpoint only.
/// Implementation lives in Infrastructure (HTTP client per provider).
/// </summary>
public interface IPaymentGatewayVerificationService
{
    /// <summary>
    /// Calls the provider's account verification endpoint using the supplied credentials.
    /// Returns account details on success; sets IsVerified = false and ErrorMessage on failure.
    /// SECURITY: credentials are used only for the HTTP call and are NOT logged.
    /// </summary>
    Task<GatewayVerificationResult> VerifyAsync(
        string provider,
        Dictionary<string, string> credentials,
        CancellationToken ct);
}
