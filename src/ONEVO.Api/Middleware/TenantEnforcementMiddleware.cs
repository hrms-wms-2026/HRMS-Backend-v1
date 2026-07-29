using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Api.Middleware;

public sealed class TenantEnforcementMiddleware
{
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
            context.Response.Cookies.Delete("onevo_session", new CookieOptions { Path = "/" });
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

    private static void ClearInvalidTenantSessionFromAnonymousRequest(
        HttpContext context,
        ITenantContext tenantContext)
    {
        if (!context.Request.Cookies.ContainsKey("onevo_session"))
            return;

        var sessionTenantClaim = context.User.FindFirstValue("tenant_id");
        var sessionMatchesHost = context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(sessionTenantClaim, out var sessionTenantId)
            && sessionTenantId == tenantContext.TenantId;

        if (sessionMatchesHost)
            return;

        context.Response.Cookies.Delete("onevo_session", new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete("onevo_csrf", new CookieOptions { Path = "/" });
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static Task WriteProblem(HttpContext ctx, int status, string detail)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { status, detail });
    }
}
