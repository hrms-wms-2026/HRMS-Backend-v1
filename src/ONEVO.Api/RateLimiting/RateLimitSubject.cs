using System.Net;
using System.Security.Claims;

namespace ONEVO.Api.RateLimiting;

/// <summary>Scheme names the limiter authenticates explicitly (UseAuthentication only runs the default one).</summary>
public static class RateLimitAuthenticationSchemes
{
    public const string Tenant = "TenantScheme";
    public const string Admin = "AdminScheme";
    public const string TrayDevice = "TrayDeviceScheme";
}

public enum RateLimitSubjectKind
{
    /// <summary>Outside /api/v1 and /admin/v1 (health, swagger, SignalR hubs): not limited.</summary>
    Exempt,
    Anonymous,
    User,
    TrayDevice,
    AdminAnonymous,
    Admin,
}

/// <summary>
/// Who a request is counted against. Resolved once per request by
/// <see cref="RateLimitSubjectMiddleware"/> and read by the global limiter's partitioner.
/// </summary>
public sealed record RateLimitSubject(RateLimitSubjectKind Kind, string Id, Guid? TenantId)
{
    private const string TrayDeviceTokenType = "tray_device";

    public static RateLimitSubject Exempt { get; } = new(RateLimitSubjectKind.Exempt, string.Empty, null);

    public static RateLimitSubject ForAnonymous(string ip) => new(RateLimitSubjectKind.Anonymous, ip, null);
    public static RateLimitSubject ForUser(Guid? tenantId, Guid userId) => new(RateLimitSubjectKind.User, userId.ToString(), tenantId);
    public static RateLimitSubject ForTrayDevice(Guid? tenantId, Guid deviceId) => new(RateLimitSubjectKind.TrayDevice, deviceId.ToString(), tenantId);
    public static RateLimitSubject ForAdminAnonymous(string ip) => new(RateLimitSubjectKind.AdminAnonymous, ip, null);
    public static RateLimitSubject ForAdmin(Guid adminUserId) => new(RateLimitSubjectKind.Admin, adminUserId.ToString(), null);

    /// <summary>
    /// The limiter partition key. It carries the settings version, so changed limits get a new
    /// limiter on the next request; the old one goes idle and is cleaned up by the framework.
    /// </summary>
    public string PartitionKey(long settingsVersion) => Kind switch
    {
        RateLimitSubjectKind.Anonymous => $"anon:{Id}:v{settingsVersion}",
        RateLimitSubjectKind.User => $"user:{TenantId}:{Id}:v{settingsVersion}",
        RateLimitSubjectKind.TrayDevice => $"tray:{Id}:v{settingsVersion}",
        RateLimitSubjectKind.AdminAnonymous => $"admin-anon:{Id}:v{settingsVersion}",
        RateLimitSubjectKind.Admin => $"admin:{Id}:v{settingsVersion}",
        _ => "exempt",
    };

    /// <summary>Key of the per-tenant safety ceiling chained after user and tray partitions.</summary>
    public static string ForTenantCeiling(Guid tenantId, long settingsVersion) => $"tenant:{tenantId}:v{settingsVersion}";

    /// <summary>True when the per-tenant ceiling also applies to this subject.</summary>
    public bool CountsAgainstTenant =>
        TenantId is not null && Kind is RateLimitSubjectKind.User or RateLimitSubjectKind.TrayDevice;

    /// <summary>
    /// Picks the subject for a request. <paramref name="principal"/> is the signed-in identity
    /// for the route family: the admin principal on /admin/v1, otherwise the tenant user or tray
    /// device principal. Anything not signed in falls back to the client IP.
    /// </summary>
    public static RateLimitSubject Resolve(PathString path, ClaimsPrincipal? principal, IPAddress? remoteIp)
    {
        var isAdmin = path.StartsWithSegments("/admin/v1", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && !path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase))
            return Exempt;

        var ip = remoteIp?.ToString() ?? "unknown";
        var signedIn = principal?.Identity?.IsAuthenticated == true;
        var hasId = Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id);

        if (isAdmin)
        {
            return signedIn && hasId
                && principal!.Identity!.AuthenticationType == RateLimitAuthenticationSchemes.Admin
                    ? ForAdmin(id)
                    : ForAdminAnonymous(ip);
        }

        if (!signedIn || !hasId)
            return ForAnonymous(ip);

        Guid? tenantId = Guid.TryParse(principal!.FindFirstValue("tenant_id"), out var t) ? t : null;
        return principal.FindFirstValue("token_type") == TrayDeviceTokenType
            ? ForTrayDevice(tenantId, id)
            : ForUser(tenantId, id);
    }
}
