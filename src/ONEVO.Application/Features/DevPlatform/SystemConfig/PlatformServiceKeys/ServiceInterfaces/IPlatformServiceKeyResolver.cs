namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

/// <summary>
/// Runtime resolver for ONEVO-owned platform service credentials.
/// Intended for future server-side consumers (e.g. transactional email/outbox sending).
/// SECURITY: returns the decrypted key to server-side callers ONLY.
/// No controller may ever expose this value in a response.
/// </summary>
public interface IPlatformServiceKeyResolver
{
    /// <summary>
    /// Decrypts and returns the API key for an ACTIVE service key row.
    /// Returns null when the service key is unknown or inactive.
    /// </summary>
    Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct);
}
