using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace ONEVO.Api.RateLimiting;

/// <summary>
/// Runs after UseAuthentication and before UseRateLimiter. UseAuthentication only fills
/// context.User from the default TenantScheme, so this authenticates the admin cookie on
/// /admin/v1 and a tray bearer token on /api/v1 explicitly. Authentication results are cached per
/// request, so the authorization step later reuses them at no extra cost. It only identifies the
/// caller for rate limiting: it never changes context.User and never authorizes anything.
/// </summary>
public sealed class RateLimitSubjectMiddleware
{
    public const string ItemKey = "ONEVO.RateLimitSubject";

    private readonly RequestDelegate _next;

    public RateLimitSubjectMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAuthenticationSchemeProvider schemes)
    {
        var path = context.Request.Path;
        ClaimsPrincipal? principal = context.User;

        if (path.StartsWithSegments("/admin/v1", StringComparison.OrdinalIgnoreCase))
        {
            principal = await TryAuthenticateAsync(context, schemes, RateLimitAuthenticationSchemes.Admin);
        }
        else if (path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
                 && principal?.Identity?.IsAuthenticated != true
                 && HasBearerToken(context.Request))
        {
            principal = await TryAuthenticateAsync(context, schemes, RateLimitAuthenticationSchemes.TrayDevice)
                ?? principal;
        }

        context.Items[ItemKey] = RateLimitSubject.Resolve(path, principal, context.Connection.RemoteIpAddress);
        await _next(context);
    }

    /// <summary>The subject resolved for this request, or one resolved from context.User.</summary>
    public static RateLimitSubject GetSubject(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) && value is RateLimitSubject subject
            ? subject
            : RateLimitSubject.Resolve(context.Request.Path, context.User, context.Connection.RemoteIpAddress);

    private static async Task<ClaimsPrincipal?> TryAuthenticateAsync(
        HttpContext context, IAuthenticationSchemeProvider schemes, string scheme)
    {
        if (await schemes.GetSchemeAsync(scheme) is null)
            return null;

        var result = await context.AuthenticateAsync(scheme);
        return result.Succeeded ? result.Principal : null;
    }

    private static bool HasBearerToken(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}
