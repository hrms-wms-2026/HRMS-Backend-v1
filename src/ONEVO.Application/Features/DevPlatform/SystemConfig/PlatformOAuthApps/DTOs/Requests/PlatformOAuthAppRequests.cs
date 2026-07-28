namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Requests;

/// <summary>
/// Configure (upsert) request for PUT /admin/v1/system-config/oauth-apps/{provider}.
/// The provider comes from the route only - this type deliberately has no Provider
/// property. OAuth protocol metadata (authorizationUrl/tokenUrl/defaultScopes) is
/// backend-owned via PlatformOAuthProviderCatalog and deliberately has no property here
/// either, so the frontend cannot set or override it.
/// ClientSecret/PrivateKey are plaintext in transit only - encrypted immediately by the
/// command handler via IEncryptionService and never stored or logged raw.
/// All fields are optional so the same endpoint supports incremental configuration
/// (e.g. set clientId now, add the secret in a later call or via rotate-secret).
/// </summary>
public sealed class ConfigurePlatformOAuthAppRequest
{
    public string? AppName { get; init; }
    public string? LogoUrl { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? PrivateKey { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>
/// Rotate request. ClientSecret/PrivateKey are plaintext in transit only - encrypted
/// immediately and never stored or logged raw.
/// </summary>
public sealed class RotatePlatformOAuthAppSecretRequest
{
    public string ClientSecret { get; init; } = string.Empty;
    public string? PrivateKey { get; init; }
}
