namespace ONEVO.Api.Extensions;

internal static class CorsExtensions
{
    internal static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        var rootDomain = configuration["Tenancy:RootDomain"];

        services.AddCors(options =>
            options.AddPolicy("AllowFrontend", policy =>
                policy.SetIsOriginAllowed(origin => IsAllowedOrigin(origin, allowedOrigins, rootDomain))
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()));

        return services;
    }

    /// <summary>
    /// Origins are allowed either by exact match against the configured AllowedOrigins list (e.g.
    /// tooling that doesn't live under the tenant root domain), or by being a "{anything}.{RootDomain}"
    /// subdomain - mirroring HostTenantResolutionMiddleware's own host check, since every tenant's
    /// frontend origin is created dynamically (one per Tenant row) and can never be enumerated in a
    /// static config list.
    /// </summary>
    private static bool IsAllowedOrigin(string origin, string[] allowedOrigins, string? rootDomain)
    {
        if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(rootDomain) || !Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        var host = originUri.Host;
        return host.Equals(rootDomain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + rootDomain, StringComparison.OrdinalIgnoreCase);
    }
}
