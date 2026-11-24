using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Auth;

/// <summary>
/// Task 15 (deny by default): the authentication scheme the authorization fallback policy uses.
///
/// The fallback policy applies to every endpoint that carries no authorization metadata at all
/// (no [Authorize], no policy, no [AllowAnonymous]). If it only checked the default TenantScheme,
/// an admin or tray endpoint that forgot its attribute would answer 401 even to a signed-in admin
/// or device. This policy scheme forwards to the one real scheme that matches the request instead,
/// so exactly one handler authenticates and exactly one handler writes the 401 challenge body
/// (challenging several cookie schemes at once would write the JSON body twice).
///
/// The fallback only requires an authenticated user. Endpoints that need a specific principal
/// must still say so with their own policy (TenantPolicy, AdminPolicy, TrayDevicePolicy).
/// </summary>
public static class FallbackAuthenticationScheme
{
    public const string Name = "FallbackScheme";

    public const string TenantScheme = "TenantScheme";
    public const string AdminScheme = "AdminScheme";
    public const string TrayDeviceScheme = "TrayDeviceScheme";

    /// <summary>
    /// Picks the scheme for a request: a bearer token (or the agent hub's access_token query
    /// value) means a tray device, an /admin path means the admin cookie, anything else the
    /// tenant cookie.
    /// </summary>
    public static string Select(HttpContext context)
    {
        var request = context.Request;

        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return TrayDeviceScheme;

        if (request.Path.StartsWithSegments("/hubs/agent-commands")
            && !string.IsNullOrEmpty(request.Query["access_token"]))
            return TrayDeviceScheme;

        if (request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
            return AdminScheme;

        return TenantScheme;
    }
}
