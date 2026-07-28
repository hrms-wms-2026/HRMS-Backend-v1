using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Resolves a payment webhook signing secret from the approved encrypted
/// payment_gateway_configs/payment_gateway_credentials storage.
/// </summary>
public sealed class PaymentGatewayWebhookSecretResolver
    : IPaymentGatewayWebhookSecretResolver
{
    private readonly IPaymentGatewayRepository _repository;
    private readonly IEncryptionService _encryption;

    public PaymentGatewayWebhookSecretResolver(
        IPaymentGatewayRepository repository,
        IEncryptionService encryption)
    {
        _repository = repository;
        _encryption = encryption;
    }

    public async Task<string?> ResolveWebhookSecretAsync(
        string provider,
        string gatewayKey,
        CancellationToken ct)
    {
        if (!PaymentGatewayKeyRules.IsValid(gatewayKey))
        {
            return null;
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var config = await _repository.GetByGatewayKeyAsync(gatewayKey, ct);
        if (config is null ||
            !config.IsActive ||
            !string.Equals(
                config.Provider,
                normalizedProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await ResolveSecretForConfigAsync(config.Id, ct);
    }

    public async Task<string?> ResolveByConfigIdAsync(
        string provider,
        Guid gatewayConfigId,
        CancellationToken ct)
    {
        if (gatewayConfigId == Guid.Empty)
        {
            return null;
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var config = await _repository.GetByIdAsync(
            gatewayConfigId,
            includeCredentials: false,
            includeRoutes: false,
            ct);

        if (config is null ||
            !config.IsActive ||
            !string.Equals(
                config.Provider,
                normalizedProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await ResolveSecretForConfigAsync(config.Id, ct);
    }

    private async Task<string?> ResolveSecretForConfigAsync(
        Guid gatewayConfigId,
        CancellationToken ct)
    {
        var credential = await _repository.GetActiveCredentialAsync(
            gatewayConfigId,
            ct);

        if (credential is null ||
            !credential.IsActive ||
            credential.WebhookSecretEncrypted is not { Length: > 0 })
        {
            return null;
        }

        var webhookSecret = _encryption.DecryptBytes(
            credential.WebhookSecretEncrypted);

        return string.IsNullOrWhiteSpace(webhookSecret)
            ? null
            : webhookSecret;
    }
}
