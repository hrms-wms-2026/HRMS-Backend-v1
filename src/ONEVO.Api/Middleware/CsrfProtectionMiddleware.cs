using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Api.Middleware;

/// <summary>
/// CSRF is bound to the server-side session, not just cookie/header equality: the header token is
/// hashed and compared against the csrf_token_hash stored on the authenticated session row (tenant
/// onevo_session or admin_session cookie). Copying a CSRF
/// cookie value alone, without a valid session cookie for that same session, cannot pass.
/// </summary>
public class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> UnsafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    // These paths are unauthenticated flows — CSRF attacks cannot target them
    // meaningfully, and blocking them when a stale session cookie exists breaks UX.
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/auth/login",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/force-change-password",
        "/api/v1/auth/mfa/verify",
        "/api/v1/auth/login/select-workspace",
        "/api/v1/auth/login/google",
        "/api/v1/auth/session-exchange",
        "/api/v1/legal/acceptances/complete-login",
        "/admin/v1/auth/login",
        "/admin/v1/auth/google-callback",
        "/admin/v1/auth/forgot-password",
        "/admin/v1/auth/reset-password",
        "/admin/v1/auth/accept-invite",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;

    public CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context, ISecureTokenGenerator tokenService)
    {
        var scope = ShouldValidate(context);
        if (scope is null)
        {
            await _next(context);
            return;
        }

        var expectedScheme = scope == CsrfScope.Admin ? "AdminScheme" : "TenantScheme";

        // UseAuthentication() only auto-populates context.User for the default scheme
        // (TenantScheme). AdminScheme is never authenticated unless requested explicitly,
        // so it must be authenticated here rather than read off the ambient context.User.
        var authenticateResult = await context.AuthenticateAsync(expectedScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            // The session cookie is present but no longer resolves to a live session (revoked,
            // expired, or left over from an earlier login). Such a request carries no ambient
            // authority for CSRF to protect, so it is treated exactly like a request with no
            // session cookie at all: authorization downstream answers 401 on protected endpoints,
            // which is what lets the SPAs recognise an ended session and return to the login
            // page. Answering 403 "CSRF" here instead left users stuck (even logout failed).
            await _next(context);
            return;
        }

        if (!IsTokenValid(context, tokenService, authenticateResult.Principal))
        {
            _logger.LogWarning(
                "Rejected request with missing or invalid CSRF token for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://onevo.com/errors/csrf",
                title = "Forbidden",
                status = 403,
                detail = "A valid CSRF token is required for this request."
            });
            return;
        }

        await _next(context);
    }

    private static bool IsTokenValid(
        HttpContext context, ISecureTokenGenerator tokenService, System.Security.Claims.ClaimsPrincipal principal)
    {
        var headerToken = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerToken))
            return false;

        var expectedHash = principal.FindFirst("csrf_token_hash")?.Value;
        if (string.IsNullOrEmpty(expectedHash))
            return false;

        return FixedTimeHashEquals(tokenService.HashToken(headerToken), expectedHash);
    }

    private static bool FixedTimeHashEquals(string a, string b)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(a), Convert.FromHexString(b));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private enum CsrfScope { Tenant, Admin }

    private static CsrfScope? ShouldValidate(HttpContext context)
    {
        var isTenantRoute = context.Request.Path.StartsWithSegments("/api/v1");
        var isAdminRoute = context.Request.Path.StartsWithSegments("/admin/v1");
        if (!isTenantRoute && !isAdminRoute)
            return null;

        if (!UnsafeMethods.Contains(context.Request.Method))
            return null;

        if (ExemptPaths.Contains(context.Request.Path.Value ?? string.Empty))
            return null;

        if (isTenantRoute)
        {
            if (context.Request.Path.StartsWithSegments("/api/v1/auth/invitations"))
                return null;

            return context.Request.Cookies.ContainsKey("onevo_session") ? CsrfScope.Tenant : null;
        }

        return context.Request.Cookies.ContainsKey("admin_session") ? CsrfScope.Admin : null;
    }
}
