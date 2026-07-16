namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

/// <summary>
/// ONEVO's OAuth app registration for an external integration provider
/// (github, google, microsoft, zoom — Slack is Phase 2).
/// These are ONEVO developer app metadata used later when tenants/employees run
/// OAuth consent flows; they are NOT tenant or user tokens.
/// Phase 1 canonical table: platform_oauth_apps.
/// Client secrets are stored separately in platform_oauth_app_credentials.
/// </summary>
public class PlatformOAuthApp
{
    public Guid Id { get; set; }

    /// <summary>Operator-set slug, stored lowercase and unique: github, google, microsoft, zoom.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>App name shown on the provider's OAuth consent screen.</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Nullable; uploaded logo URL.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>OAuth client id. Not encrypted — it is used in redirect URLs.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string AuthorizationUrl { get; set; } = string.Empty;

    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>Default OAuth scopes requested during consent. Postgres text[].</summary>
    public string[] DefaultScopes { get; set; } = Array.Empty<string>();

    /// <summary>Only active apps may be resolved for runtime consent flows.</summary>
    public bool IsActive { get; set; }

    /// <summary>When the app registration last passed verification. Nullable.</summary>
    public DateTimeOffset? LastVerifiedAt { get; set; }

    /// <summary>Platform user who last created/updated this registration. FK -> platform_users.</summary>
    public Guid UpdatedById { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
