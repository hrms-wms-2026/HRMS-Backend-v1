namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Requests;

/// <summary>
/// Create request. ClientSecret/PrivateKey are plaintext in transit only — encrypted
/// immediately by the command handler via IEncryptionService and never stored or logged raw.
/// </summary>
public sealed class CreatePlatformOAuthAppRequest
{
    public string Provider { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string? PrivateKey { get; init; }
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string TokenUrl { get; init; } = string.Empty;
    public string[] DefaultScopes { get; init; } = Array.Empty<string>();
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Metadata-only update. Secret changes must go through rotate-secret;
/// this request deliberately has no clientSecret/privateKey fields.
/// </summary>
public sealed class UpdatePlatformOAuthAppRequest
{
    public string AppName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string TokenUrl { get; init; } = string.Empty;
    public string[] DefaultScopes { get; init; } = Array.Empty<string>();
    public bool IsActive { get; init; }
}

/// <summary>
/// Rotate request. ClientSecret/PrivateKey are plaintext in transit only — encrypted
/// immediately and never stored or logged raw.
/// </summary>
public sealed class RotatePlatformOAuthAppSecretRequest
{
    public string ClientSecret { get; init; } = string.Empty;
    public string? PrivateKey { get; init; }
}
