namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;

/// <summary>
/// OAuth app list/detail response.
/// SECURITY: neither plaintext secrets nor *_encrypted columns are EVER included.
/// Credential presence is exposed as booleans + version number only.
/// </summary>
public sealed class PlatformOAuthAppDto
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string TokenUrl { get; init; } = string.Empty;
    public string[] DefaultScopes { get; init; } = Array.Empty<string>();
    public bool IsActive { get; init; }
    public DateTimeOffset? LastVerifiedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool HasActiveCredential { get; init; }
    public int? ActiveCredentialVersion { get; init; }
    public bool HasPrivateKey { get; init; }
}

/// <summary>
/// Result of local metadata verification. No secret material is ever included.
/// This step performs NO live provider API calls.
/// </summary>
public sealed class OAuthAppVerificationResultDto
{
    public string Provider { get; init; } = string.Empty;

    /// <summary>"healthy" or "error".</summary>
    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>Set when the local validation passed; null on error.</summary>
    public DateTimeOffset? VerifiedAt { get; init; }
}
