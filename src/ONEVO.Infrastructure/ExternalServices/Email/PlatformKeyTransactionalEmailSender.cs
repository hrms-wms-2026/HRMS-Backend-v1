using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Transactional email sender backed by ONEVO-owned platform service keys.
/// - Provider comes from non-secret Email:Provider config ("sendgrid"/"resend",
///   default "sendgrid" when empty).
/// - The API key is resolved per send from platform_service_keys via
///   IPlatformServiceKeyResolver, decrypted in memory only, never cached, never logged.
/// - When no active key is configured there is NO appsettings fallback: the attempt
///   fails with a safe error that contains no secret material.
/// </summary>
public sealed class PlatformKeyTransactionalEmailSender : ITransactionalEmailSender
{
    private readonly IPlatformServiceKeyResolver _keyResolver;
    private readonly IEnumerable<IEmailProviderAdapter> _adapters;
    private readonly EmailOptions _options;
    private readonly ILogger<PlatformKeyTransactionalEmailSender> _logger;

    public PlatformKeyTransactionalEmailSender(
        IPlatformServiceKeyResolver keyResolver,
        IEnumerable<IEmailProviderAdapter> adapters,
        IOptions<EmailOptions> options,
        ILogger<PlatformKeyTransactionalEmailSender> logger)
    {
        _keyResolver = keyResolver;
        _adapters = adapters;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TransactionalEmailResult> SendAsync(
        TransactionalEmailRequest request,
        CancellationToken ct = default)
    {
        // Step 1: resolve the configured provider (non-secret selector, default sendgrid).
        var provider = string.IsNullOrWhiteSpace(_options.Provider)
            ? PlatformServiceKeyCatalog.Sendgrid
            : _options.Provider.Trim().ToLowerInvariant();

        if (provider != PlatformServiceKeyCatalog.Sendgrid &&
            provider != PlatformServiceKeyCatalog.Resend)
        {
            return TransactionalEmailResult.Failed(provider,
                $"Unsupported email provider '{provider}'. Allowed values: sendgrid, resend.");
        }

        // Step 2: validate non-secret sender configuration.
        if (string.IsNullOrWhiteSpace(_options.FromAddress))
            return TransactionalEmailResult.Failed(provider, "Email:FromAddress is not configured.");
        if (string.IsNullOrWhiteSpace(_options.FromName))
            return TransactionalEmailResult.Failed(provider, "Email:FromName is not configured.");
        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            return TransactionalEmailResult.Failed(provider, "Recipient email is empty.");

        IEmailProviderAdapter? adapter = null;
        foreach (var candidate in _adapters)
        {
            if (string.Equals(candidate.Provider, provider, StringComparison.Ordinal))
            {
                adapter = candidate;
                break;
            }
        }
        if (adapter is null)
        {
            return TransactionalEmailResult.Failed(provider,
                $"No email adapter registered for provider '{provider}'.");
        }

        // Step 3: resolve the active ONEVO-owned key from platform_service_keys.
        // No appsettings fallback exists by design.
        var apiKey = await _keyResolver.ResolveActiveKeyAsync(provider, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "Transactional email send blocked: no active platform service key for provider {Provider}.",
                provider);
            return TransactionalEmailResult.Failed(provider,
                $"Active platform service key not configured for provider '{provider}'.");
        }

        // Step 4: call the provider adapter. The decrypted key stays in memory inside
        // the adapter call and is never logged or returned.
        var result = await adapter.SendAsync(apiKey, _options, request, ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "Transactional email sent to {Recipient} via {Provider} (provider message id: {ProviderMessageId}).",
                request.RecipientEmail, result.Provider, result.ProviderMessageId ?? "n/a");
        }

        return result;
    }
}
