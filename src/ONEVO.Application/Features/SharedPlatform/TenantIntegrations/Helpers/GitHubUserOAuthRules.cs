namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

public static class GitHubUserOAuthRules
{
    public const string IntegrationKey = "github";
    public const string Provider = "github";

    public static string? ValidateReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var value = returnUrl.Trim();
        if (!value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\'))
        {
            return null;
        }

        return value;
    }

    public static string BuildAuthorizationUrl(
        string authorizationUrl,
        string clientId,
        string redirectUri,
        IReadOnlyList<string> scopes,
        string state)
    {
        var separator = authorizationUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return authorizationUrl + separator +
            "client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
            "&scope=" + Uri.EscapeDataString(string.Join(' ', scopes)) +
            "&state=" + Uri.EscapeDataString(state);
    }
}
