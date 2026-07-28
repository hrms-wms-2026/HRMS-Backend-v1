using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Transactional email sender backed by ONEVO-owned platform service keys.
/// - Provider selection is entirely DB-backed: the active transactional email
///   provider is resolved by joining active transactional_email rows in
///   platform_providers to matching active platform_service_keys credentials.
///   There is no configuration-based provider selector and no default provider.
/// - Zero active providers and more than one active provider both fail safely with
///   no secret material; the backend never guesses between candidates.
/// - The API key is decrypted in memory only for the duration of the send and is
///   never cached or logged.
/// </summary>
public sealed class PlatformKeyTransactionalEmailSender : ITransactionalEmailSender
{
    /// <summary>Provider slot used on TransactionalEmailResult.Failed when no provider was resolved.</summary>
    private const string NoProviderResolved = "unresolved";

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
        // Step 1: validate non-secret sender configuration before touching the database.
        if (string.IsNullOrWhiteSpace(_options.FromAddress))
            return TransactionalEmailResult.Failed(NoProviderResolved, "Email:FromAddress is not configured.");
        if (string.IsNullOrWhiteSpace(_options.FromName))
            return TransactionalEmailResult.Failed(NoProviderResolved, "Email:FromName is not configured.");
        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            return TransactionalEmailResult.Failed(NoProviderResolved, "Recipient email is empty.");

        // Step 2: resolve the single active transactional email provider from
        // platform_service_keys. No appsettings fallback exists by design, and the
        // backend never picks one arbitrarily when zero or multiple rows are active.
        var resolution = await _keyResolver.ResolveActiveTransactionalEmailProviderAsync(ct);

        if (resolution.Status == TransactionalEmailProviderResolutionStatus.NotConfigured)
        {
            _logger.LogWarning(
                "Transactional email send blocked: no active configured transactional email provider.");
            return TransactionalEmailResult.Failed(
                NoProviderResolved, TransactionalEmailFailureCodes.ProviderUnavailable);
        }

        if (resolution.Status == TransactionalEmailProviderResolutionStatus.Ambiguous)
        {
            _logger.LogWarning(
                "Transactional email send blocked: more than one active configured transactional email provider.");
            return TransactionalEmailResult.Failed(
                NoProviderResolved, TransactionalEmailFailureCodes.ProviderAmbiguous);
        }

        var provider = resolution.ProviderKey!;
        var apiKey = resolution.ApiKey!;

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

        // Step 3: call the provider adapter. The decrypted key stays in memory inside
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
