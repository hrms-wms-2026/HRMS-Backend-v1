using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;

public static class IntegrationCatalogRules
{
    private static readonly Regex Slug = new("^[a-z][a-z0-9_]{0,49}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "slack",
        "resend",
        "sendgrid",
        "cloudflare",
        "r2",
        "stripe",
        "paddle",
        "payhere",
        "biometric",
        "biometric_terminal",
        "fcm"
    };

    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidSlug(string value) => !string.IsNullOrWhiteSpace(value) && Slug.IsMatch(value);

    public static bool IsForbidden(string value)
    {
        var normalizedValue = Normalize(value);
        return Forbidden.Contains(normalizedValue)
            || normalizedValue.StartsWith("slack_", StringComparison.Ordinal);
    }

    public static bool IsValidScope(string value)
    {
        return value is "tenant" or "user" or "both";
    }

    public static string? ValidateMetadata(string displayName, string connectionScope, string provider, string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
        {
            return "displayName is required and must be at most 100 characters.";
        }

        if (!IsValidScope(connectionScope))
        {
            return "connectionScope must be exactly 'tenant', 'user', or 'both'.";
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return "onevoAppProvider is required.";
        }

        if (IsForbidden(provider))
        {
            return "This provider is not permitted in the Phase 1 integration catalog.";
        }

        if (logoUrl?.Length > 500)
        {
            return "logoUrl must be at most 500 characters.";
        }

        return null;
    }
}
