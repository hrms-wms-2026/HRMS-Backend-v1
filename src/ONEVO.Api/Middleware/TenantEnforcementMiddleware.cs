using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Api.Middleware;

public sealed class TenantEnforcementMiddleware
{
    /// <summary>
    /// Returned when a tenant session reaches a tenant API route without a tenant context: the root
    /// host in Subdomain mode, or a request without the tenant header in SingleHost mode.
    /// </summary>
    public const string TenantSessionWithoutTenantContextDetail =
        "Tenant session requires a tenant context. Send the request to the tenant host or include the tenant header.";

    private readonly RequestDelegate _next;

    public TenantEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantCtx = context.RequestServices.GetRequiredService<ITenantContext>();
        var path = context.Request.Path;
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var allowsAnonymous = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        var isTenantApiPath = path.StartsWithSegments("/api/v1");
        var isAdminPath = path.StartsWithSegments("/admin");

        if (tenantCtx.ContextMode == TenantContextMode.Admin && isTenantApiPath)
        {
            await WriteProblem(context, 403, "Admin host cannot access tenant API routes.");
            return;
        }

        if (tenantCtx.ContextMode == TenantContextMode.Tenant && isAdminPath)
        {
            await WriteProblem(context, 403, "Tenant host cannot access admin routes.");
            return;
        }

        if (tenantCtx.ContextMode != TenantContextMode.Tenant)
        {
            // A tenant session in System context (root host, or SingleHost without the tenant
            // header) would run with app.tenant_context_mode = 'system', where the TenantOrSystem
            // and PlatformShared RLS predicates match rows of every tenant. Base-domain login,
            // workspace selection, legal completion and session exchange are [AllowAnonymous];
            // logout only clears the caller's own cookies, so it stays allowed.
            if (tenantCtx.ContextMode == TenantContextMode.System
                && isTenantApiPath
                && !allowsAnonymous
                && isAuthenticated
                && context.User.FindFirst("tenant_id") is not null
                && !IsLogout(context.Request))
            {
                await WriteProblem(context, 403, TenantSessionWithoutTenantContextDetail);
                return;
            }

            await _next(context);
            return;
        }

        if (allowsAnonymous)
        {
            ClearInvalidTenantSessionFromAnonymousRequest(context, tenantCtx);
            await _next(context);
            return;
        }

        if (!isAuthenticated)
        {
            await WriteProblem(context, 401, "Authentication required.");
            return;
        }

        var jwtTenantClaim = context.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(jwtTenantClaim, out var jwtTenantId) ||
            jwtTenantId != tenantCtx.TenantId)
        {
            context.Response.Cookies.Delete("crownus_session", new CookieOptions { Path = "/" });
            await WriteProblem(context, 403, "Token tenant does not match request host tenant.");
            return;
        }

        if (tenantCtx.Status is TenantStatus.Suspended)
        {
            await WriteProblem(context, 403, "Tenant is suspended.");
            return;
        }

        if (tenantCtx.Status is TenantStatus.Cancelled)
        {
            await WriteProblem(context, 403, "Tenant account has been cancelled.");
            return;
        }

        if (tenantCtx.Status is TenantStatus.Provisioning)
        {
            await WriteProblem(context, 403, "Tenant is not yet active.");
            return;
        }

        await _next(context);
    }

    private static bool IsLogout(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && string.Equals(request.Path.Value?.TrimEnd('/'), "/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase);

    private static void ClearInvalidTenantSessionFromAnonymousRequest(
        HttpContext context,
        ITenantContext tenantContext)
    {
        if (!context.Request.Cookies.ContainsKey("crownus_session"))
            return;

        var sessionTenantClaim = context.User.FindFirstValue("tenant_id");
        var sessionMatchesHost = context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(sessionTenantClaim, out var sessionTenantId)
            && sessionTenantId == tenantContext.TenantId;

        if (sessionMatchesHost)
            return;

        context.Response.Cookies.Delete("crownus_session", new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete("crownus_csrf", new CookieOptions { Path = "/" });
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static Task WriteProblem(HttpContext ctx, int status, string detail)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { status, detail });
    }
}
