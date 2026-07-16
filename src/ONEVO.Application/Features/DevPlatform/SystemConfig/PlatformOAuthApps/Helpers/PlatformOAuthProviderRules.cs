using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;

/// <summary>
/// Validation rules for ONEVO OAuth app registrations.
/// Providers are operator-set lowercase slugs (docs: lowercase, hyphens; e.g. github,
/// google, microsoft, zoom). Slack is Phase 2 and must be rejected in Phase 1.
/// </summary>
public static class PlatformOAuthProviderRules
{
    /// <summary>Phase 2 providers that must not be registered yet.</summary>
    public const string SlackProvider = "slack";

    // varchar(30): lowercase letters/digits, hyphen separators, starts with a letter.
    private static readonly Regex SlugPattern = new("^[a-z][a-z0-9-]{0,29}$", RegexOptions.Compiled);

    public static string Normalize(string provider)
        => (provider ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidSlug(string provider)
        => !string.IsNullOrWhiteSpace(provider) && SlugPattern.IsMatch(provider);

    public static bool IsPhase2Provider(string provider)
        => provider == SlackProvider;

    public static bool IsAbsoluteHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
