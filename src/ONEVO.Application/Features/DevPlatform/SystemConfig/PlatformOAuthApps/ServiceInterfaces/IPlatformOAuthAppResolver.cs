namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

/// <summary>
/// Server-side resolver for ONEVO OAuth app registrations, for FUTURE tenant/user
/// OAuth connect flows. Decrypts secret material in memory only.
/// SECURITY: the returned models are internal-only - they must never be serialized
/// into an API response or a log. No controller may depend on this interface.
/// </summary>
public interface IPlatformOAuthAppResolver
{
    /// <summary>
    /// Resolves non-secret consent-flow config for an active app.
    /// Returns null when the provider is unknown or the app is inactive.
    /// </summary>
    Task<ResolvedPlatformOAuthApp?> GetActiveAppForProviderAsync(string provider, CancellationToken ct);

    /// <summary>
    /// Resolves the DECRYPTED active credential for an active app.
    /// Returns null when the provider is unknown, the app is inactive,
    /// or no active credential exists.
    /// </summary>
    Task<ResolvedPlatformOAuthAppCredential?> GetActiveCredentialForProviderAsync(string provider, CancellationToken ct);
}

/// <summary>Internal-only resolved app config. Contains no secret material.</summary>
public sealed record ResolvedPlatformOAuthApp(
    string Provider,
    string ClientId,
    string AuthorizationUrl,
    string TokenUrl,
    string[] DefaultScopes);

/// <summary>
/// Internal-only resolved credential. ClientSecret/PrivateKey are DECRYPTED plaintext -
/// server-side use only, never expose through any API DTO or log.
/// </summary>
public sealed record ResolvedPlatformOAuthAppCredential(
    string Provider,
    string ClientId,
    string ClientSecret,
    string? PrivateKey,
    int CredentialVersion);
