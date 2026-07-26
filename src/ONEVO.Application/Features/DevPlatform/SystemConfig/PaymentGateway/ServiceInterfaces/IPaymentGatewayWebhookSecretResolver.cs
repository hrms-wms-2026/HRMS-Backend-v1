namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;

/// <summary>
/// Internal-only resolver for payment-provider webhook signing secrets.
/// Implementations must resolve encrypted database credentials, decrypt only in
/// server memory, and never return the plaintext through an API response or log.
/// </summary>
public interface IPaymentGatewayWebhookSecretResolver
{
    /// <summary>
    /// Resolves the webhook signing secret for one exact provider + gateway key.
    /// Missing, inactive, mismatched, or malformed ownership fails closed with
    /// <see langword="null"/>.
    /// </summary>
    Task<string?> ResolveWebhookSecretAsync(
        string provider,
        string gatewayKey,
        CancellationToken ct);

    /// <summary>
    /// Resolves the webhook signing secret for one exact provider + gateway config ID.
    /// Missing, inactive, mismatched provider, or missing active credential fails closed with
    /// <see langword="null"/>.
    /// </summary>
    Task<string?> ResolveByConfigIdAsync(
        string provider,
        Guid gatewayConfigId,
        CancellationToken ct);
}
