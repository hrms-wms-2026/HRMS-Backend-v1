namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

public static class TenantIntegrationCredentialRules
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "connected", "error", "expired", "disconnected", "disabled"
    };

    public static string NormalizeIntegrationKey(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool IsAllowedStatus(string status) => AllowedStatuses.Contains(status);
}
