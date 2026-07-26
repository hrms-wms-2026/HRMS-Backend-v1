namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

/// <summary>Outcome discriminator for <see cref="TransactionalEmailProviderResolution"/>.</summary>
public enum TransactionalEmailProviderResolutionStatus
{
    /// <summary>Zero active catalog/email-credential matches.</summary>
    NotConfigured,

    /// <summary>More than one active catalog/email-credential match.</summary>
    Ambiguous,

    /// <summary>Exactly one active sendgrid/resend row; ApiKey is populated.</summary>
    Resolved
}

/// <summary>
/// Outcome of resolving the single active transactional email provider from
/// platform_providers joined to platform_service_keys. An explicit result type is used instead of a nullable
/// string so "not configured" and "ambiguous" are distinguishable outcomes - the
/// backend must never guess between multiple active candidates.
/// SECURITY: ApiKey is populated only when Status is Resolved and must never be
/// logged or returned outside the server-side send call path.
/// </summary>
public sealed class TransactionalEmailProviderResolution
{
    public TransactionalEmailProviderResolutionStatus Status { get; }
    public string? ProviderKey { get; }
    public string? ApiKey { get; }

    private TransactionalEmailProviderResolution(
        TransactionalEmailProviderResolutionStatus status, string? providerKey, string? apiKey)
    {
        Status = status;
        ProviderKey = providerKey;
        ApiKey = apiKey;
    }

    public static TransactionalEmailProviderResolution NotConfigured() =>
        new(TransactionalEmailProviderResolutionStatus.NotConfigured, null, null);

    public static TransactionalEmailProviderResolution Ambiguous() =>
        new(TransactionalEmailProviderResolutionStatus.Ambiguous, null, null);

    public static TransactionalEmailProviderResolution Resolved(string providerKey, string apiKey) =>
        new(TransactionalEmailProviderResolutionStatus.Resolved, providerKey, apiKey);
}

/// <summary>
/// Runtime resolver for ONEVO-owned platform service credentials.
/// Intended for server-side consumers only (transactional email, Cloudflare R2).
/// SECURITY: returns decrypted keys to server-side callers ONLY.
/// No controller may ever expose this value in a response.
/// </summary>
public interface IPlatformServiceKeyResolver
{
    /// <summary>
    /// Decrypts and returns the API key for an ACTIVE service key row.
    /// Returns null when the service key is unknown or inactive.
    /// </summary>
    Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct);

    /// <summary>
    /// Resolves the single active transactional_email catalog provider with a
    /// matching active platform_service_keys credential. Zero matches returns
    /// NotConfigured; more than one returns Ambiguous; exactly one returns Resolved.
    /// </summary>
    Task<TransactionalEmailProviderResolution> ResolveActiveTransactionalEmailProviderAsync(CancellationToken ct);
}
