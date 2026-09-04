using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace ONEVO.Api.Controllers.Tenant.Auth;

/// <summary>
/// Owns the non-authoritative onevo_last_tenant hint cookie set across the parent domain
/// so the root marketing/landing page can render a "Go to Workspace" shortcut.
/// Deliberately isolated from TenantAuthResponseWriter so that tenant session/auth cookie
/// invariants (never domain-scoped) remain strictly guarded.
/// </summary>
internal static class TenantHintCookieWriter
{
    public static void SetLastTenantHintCookie(
        this ControllerBase controller, string tenantSlug, string rootDomain, IWebHostEnvironment env)
    {
        controller.Response.Cookies.Append("onevo_last_tenant", tenantSlug, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Domain = "." + rootDomain,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(180)
        });
    }

    public static void ClearLastTenantHintCookie(
        this ControllerBase controller, string rootDomain, IWebHostEnvironment env)
    {
        controller.Response.Cookies.Delete("onevo_last_tenant", new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Domain = "." + rootDomain,
            Path = "/"
        });
    }
}
