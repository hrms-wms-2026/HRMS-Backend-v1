namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;

/// <summary>
/// OAuth provider card: backend-owned catalog metadata merged with whatever operator
/// configuration exists in platform_oauth_apps / platform_oauth_app_credentials.
/// Returned for every approved provider (github, google, microsoft, zoom) even when no
/// database row exists yet, so the UI can render an unconfigured provider card.
/// SECURITY: neither plaintext secrets nor *_encrypted columns are EVER included.
/// Credential presence is exposed as booleans + version number only.
/// </summary>
public sealed class PlatformOAuthAppDto
{
    /// <summary>Catalog provider key: github, google, microsoft, or zoom.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Catalog display name, always present even when unconfigured.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Operator-set app name shown on the provider consent screen. Null until configured.</summary>
    public string? AppName { get; init; }

    public string? LogoUrl { get; init; }

    /// <summary>True only when a usable clientId (and active credential, if required) exists.</summary>
    public bool Configured { get; init; }

    public bool IsActive { get; init; }

    public string? ClientId { get; init; }

    /// <summary>Backend-owned protocol metadata - never accepted from a request body.</summary>
    public string AuthorizationUrl { get; init; } = string.Empty;

    public string TokenUrl { get; init; } = string.Empty;

    public string[] DefaultScopes { get; init; } = Array.Empty<string>();

    /// <summary>Backend-owned capability list, e.g. admin_sso, user_oauth, calendar.</summary>
    public string[] Capabilities { get; init; } = Array.Empty<string>();

    public bool ClientSecretRequired { get; init; }

    public bool HasActiveCredential { get; init; }

    public int? ActiveCredentialVersion { get; init; }

    public bool HasPrivateKey { get; init; }

    public DateTimeOffset? LastVerifiedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Result of local configuration validation. No secret material is ever included.
/// This step performs NO live provider API calls - see verificationType.
/// </summary>
public sealed class OAuthAppValidateConfigResultDto
{
    public string Provider { get; init; } = string.Empty;

    /// <summary>"valid" or "error".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Always "local" - no Google/GitHub/Microsoft/Zoom API call is made by this endpoint.</summary>
    public string VerificationType { get; init; } = "local";

    public string Message { get; init; } = string.Empty;

    /// <summary>Set when the local validation passed; null on error.</summary>
    public DateTimeOffset? VerifiedAt { get; init; }
}
