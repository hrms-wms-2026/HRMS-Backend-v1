using Microsoft.Extensions.Configuration;

namespace ONEVO.Application.Common.Tenancy;

/// <summary>How the tenant is resolved for a request.</summary>
public enum TenancyMode
{
    /// <summary>Production: tenant comes from the request subdomain.</summary>
    Subdomain,

    /// <summary>Test mode: tenant comes from the X-Crownus-Tenant header.</summary>
    SingleHost
}

public static class TenancyModeReader
{
    public const string ConfigKey = "Tenancy:Mode";

    public static TenancyMode Read(IConfiguration configuration)
    {
        var raw = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(raw))
            return TenancyMode.Subdomain;

        var value = raw.Trim();
        if (value.Equals(nameof(TenancyMode.Subdomain), StringComparison.OrdinalIgnoreCase))
            return TenancyMode.Subdomain;
        if (value.Equals(nameof(TenancyMode.SingleHost), StringComparison.OrdinalIgnoreCase))
            return TenancyMode.SingleHost;

        throw new InvalidOperationException(
            $"Invalid {ConfigKey} value '{raw}'. Expected 'Subdomain' or 'SingleHost'.");
    }
}

public static class TenancyHeaders
{
    public const string Tenant = "X-Crownus-Tenant";
    public const string HubQueryKey = "tenant";
}
