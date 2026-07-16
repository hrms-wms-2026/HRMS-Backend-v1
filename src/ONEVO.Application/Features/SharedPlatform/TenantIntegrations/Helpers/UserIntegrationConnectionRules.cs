namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

public static class UserIntegrationConnectionRules
{
    public static string NormalizeIntegrationKey(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
