namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;

/// <summary>
/// Backend-owned metadata for the OAuth providers ONEVO-HR approves in Phase 1:
/// github, google, microsoft, zoom. Slack is Phase 2 and deliberately absent here.
/// This is intentionally a plain in-code catalog, not a provider_catalog table and not
/// a C# enum: OAuth protocol metadata (authorization/token URLs, default scopes) is
/// security-sensitive and must never be accepted from the frontend or the database's
/// operator-writable columns. The database (platform_oauth_apps) stores only the
/// operator-configurable slice (client id, app name, logo, is_active) plus encrypted
/// credentials; this catalog is the single source of truth for everything else.
/// </summary>
public static class PlatformOAuthProviderCatalog
{
    public const string CapabilityAdminSso = "admin_sso";
    public const string CapabilityUserOAuth = "user_oauth";
    public const string CapabilityCalendar = "calendar";

    private static readonly IReadOnlyDictionary<string, PlatformOAuthProviderDefinition> Definitions =
        new[]
        {
            new PlatformOAuthProviderDefinition(
                Provider: "google",
                DisplayName: "Google",
                AuthorizationUrl: "https://accounts.google.com/o/oauth2/v2/auth",
                TokenUrl: "https://oauth2.googleapis.com/token",
                // "calendar" (not the narrower "calendar.readonly") since Calendar's two_way/
                // push_only sync modes need write access, not just read.
                DefaultScopes: new[] { "openid", "profile", "email", "https://www.googleapis.com/auth/calendar" },
                ClientSecretRequired: true,
                Capabilities: new[] { CapabilityAdminSso, CapabilityUserOAuth, CapabilityCalendar }),

            // GitHub default scope: OneVo-HR's system-config end-to-end-logic.md shows
            // "repo read:org" only as an "e.g." illustration of the generic Default
            // Scopes field, not as a normative requirement, and the Work Management
            // GitHub integration docs (modules/work-management/integrations/
            // end-to-end-logic.md) describe "required scopes" abstractly without a
            // literal scope list. No OneVo-HR doc clearly requires repo/read:org as the
            // default. Default to the minimal scope needed for GitHub login/connection
            // identity only. Broader scopes (repo, read:org) for Work repository sync
            // must be requested by that feature's own permission flow, not this
            // provider-wide default.
            new PlatformOAuthProviderDefinition(
                Provider: "github",
                DisplayName: "GitHub",
                AuthorizationUrl: "https://github.com/login/oauth/authorize",
                TokenUrl: "https://github.com/login/oauth/access_token",
                DefaultScopes: new[] { "read:user" },
                ClientSecretRequired: true,
                Capabilities: new[] { CapabilityUserOAuth }),

            new PlatformOAuthProviderDefinition(
                Provider: "microsoft",
                DisplayName: "Microsoft",
                AuthorizationUrl: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                TokenUrl: "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                // Calendars.ReadWrite (not the narrower Calendars.Read) since Calendar's
                // two_way/push_only sync modes need write access, not just read.
                DefaultScopes: new[] { "openid", "profile", "email", "offline_access", "Calendars.ReadWrite" },
                ClientSecretRequired: true,
                Capabilities: new[] { CapabilityUserOAuth, CapabilityCalendar }),

            new PlatformOAuthProviderDefinition(
                Provider: "zoom",
                DisplayName: "Zoom",
                AuthorizationUrl: "https://zoom.us/oauth/authorize",
                TokenUrl: "https://zoom.us/oauth/token",
                DefaultScopes: new[] { "meeting:read" },
                ClientSecretRequired: true,
                Capabilities: new[] { CapabilityUserOAuth })
        }.ToDictionary(d => d.Provider, StringComparer.Ordinal);

    /// <summary>Phase 2 providers that are deliberately not approved yet.</summary>
    private static readonly IReadOnlySet<string> Phase2Providers = new HashSet<string>(StringComparer.Ordinal)
    {
        "slack"
    };

    public static bool TryGet(string provider, out PlatformOAuthProviderDefinition definition)
        => Definitions.TryGetValue(provider, out definition!);

    public static bool IsApproved(string provider) => Definitions.ContainsKey(provider);

    public static bool IsPhase2(string provider) => Phase2Providers.Contains(provider);

    public static IReadOnlyList<PlatformOAuthProviderDefinition> GetAll()
        => Definitions.Values.OrderBy(d => d.Provider, StringComparer.Ordinal).ToList();
}

/// <summary>
/// One approved OAuth provider's backend-owned protocol metadata.
/// Never includes client id, client secret, or any operator/tenant-specific value.
/// </summary>
public sealed record PlatformOAuthProviderDefinition(
    string Provider,
    string DisplayName,
    string AuthorizationUrl,
    string TokenUrl,
    string[] DefaultScopes,
    bool ClientSecretRequired,
    string[] Capabilities);
