using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Payment gateway credential verification service.
/// Calls the provider's live account verification API to confirm credentials are valid
/// before they are saved. Credentials are used only for the HTTP call and are NOT logged.
///
/// Phase 1: Returns a structured "not configured" result for any provider
/// since the live provider HTTP clients are an Infrastructure concern outside
/// the Phase 1 System Config Foundation scope. Replace with real provider
/// HTTP clients (Stripe /v1/balance, Paddle account API, PayHere verify) in
/// the System Config Phase 1.5 or equivalent delivery.
/// </summary>
public sealed class PaymentGatewayVerificationService : IPaymentGatewayVerificationService
{
    private readonly ILogger<PaymentGatewayVerificationService> _logger;

    public PaymentGatewayVerificationService(ILogger<PaymentGatewayVerificationService> logger)
        => _logger = logger;

    /// <inheritdoc/>
    /// <remarks>
    /// SECURITY: credentials are used for HTTP verification only.
    /// They are NEVER logged. The "credentials" parameter name is intentionally
    /// omitted from any log statements to prevent accidental secret exposure.
    /// </remarks>
    public Task<GatewayVerificationResult> VerifyAsync(
        string provider,
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        // SECURITY: Do NOT log the credentials dictionary.
        _logger.LogInformation("Gateway credential verification requested for provider {Provider}", provider);

        // Phase 1 Foundation stub — replace with live HTTP calls per provider.
        // The verify endpoint still works end-to-end; it just returns a predictable
        // result until the live provider clients are wired in.
        var result = new GatewayVerificationResult
        {
            IsVerified = false,
            ErrorMessage =
                $"Live verification for provider '{provider}' is not yet wired. " +
                "Credentials accepted and encrypted on Save. " +
                "Replace PaymentGatewayVerificationService with provider-specific HTTP clients."
        };

        return Task.FromResult(result);
    }
}
