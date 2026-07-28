using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Infrastructure.Identity.Sessions;

namespace ONEVO.Api.Controllers.Tenant.Auth;

/// <summary>
/// Owns every cookie/sign-in side effect shared across the tenant Auth.* controllers, so no
/// controller reimplements onevo_session/onevo_csrf/onevo_mfa/onevo_legal_* cookie behavior.
/// </summary>
internal static class TenantAuthResponseWriter
{
    public static async Task<IActionResult> HandleSessionResultAsync(
        this ControllerBase controller, Result<LoginResponseDto> result, IWebHostEnvironment env)
    {
        if (!result.IsSuccess)
            return controller.Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;

        if (dto.RequiresPasswordChange)
            return controller.StatusCode(202, controller.WithContinueUrl(dto, dto.ToSessionResponse()));

        if (dto.RequiresMfa)
        {
            controller.SetMfaChallengeCookie(dto.MfaChallenge, env);
            return controller.StatusCode(202, controller.WithContinueUrl(dto, dto.ToSessionResponse()));
        }

        if (dto.RequiresLegalAcceptance)
        {
            controller.SetLegalPendingCookies(dto.LegalChallenge, dto.LegalCsrfToken, env);
            return controller.StatusCode(202, controller.WithContinueUrl(dto, dto.ToSessionResponse()));
        }

        await controller.SignInAsync(dto, env);
        return controller.Ok(dto.ToSessionResponse());
    }

    public static async Task SignInAsync(
        this ControllerBase controller, LoginResponseDto dto, IWebHostEnvironment env)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, dto.User!.UserId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));

        var props = new AuthenticationProperties
        {
            ExpiresUtc = dto.ExpiresAt
        };
        props.Items[TenantDatabaseTicketStore.TenantIdItemKey] = dto.User.TenantId.ToString();
        props.Items[TenantDatabaseTicketStore.CsrfTokenHashItemKey] = dto.CsrfTokenHash;

        await controller.HttpContext.SignInAsync("TenantScheme", principal, props);

        controller.Response.Cookies.Append("onevo_csrf", dto.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = dto.ExpiresAt
        });
    }

    public static void ClearInvalidTenantSessionCookies(this ControllerBase controller, IWebHostEnvironment env)
    {
        var hasSessionCookie = controller.Request.Cookies.ContainsKey("onevo_session");
        var isAuthenticated = controller.User.Identity?.IsAuthenticated == true;

        if (!hasSessionCookie || isAuthenticated)
            return;

        controller.DeleteTenantCookie("onevo_session", httpOnly: true, env);
        controller.DeleteTenantCookie("onevo_csrf", httpOnly: false, env);
    }

    public static void SetMfaChallengeCookie(
        this ControllerBase controller, string? mfaChallenge, IWebHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(mfaChallenge))
            throw new InvalidOperationException("MFA challenge material was not created.");

        controller.Response.Cookies.Append("onevo_mfa", mfaChallenge, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth/mfa/verify",
            Domain = controller.GetPendingCookieDomain(),
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        });
    }

    public static void SetLegalPendingCookies(
        this ControllerBase controller, string? legalChallenge, string? legalCsrfToken, IWebHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(legalChallenge) || string.IsNullOrWhiteSpace(legalCsrfToken))
            throw new InvalidOperationException("Legal acceptance challenge material was not created.");

        var expires = DateTimeOffset.UtcNow.AddMinutes(10);

        controller.Response.Cookies.Append("onevo_legal_pending", legalChallenge, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/legal/acceptances/complete-login",
            Domain = controller.GetPendingCookieDomain(),
            Expires = expires
        });

        // Readable double-submit CSRF value for the accept mutation. Mirrors onevo_csrf: never
        // HttpOnly, since same-origin JS must read it to attach to the follow-up request body.
        controller.Response.Cookies.Append("onevo_legal_csrf", legalCsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/legal/acceptances/complete-login",
            Domain = controller.GetPendingCookieDomain(),
            Expires = expires
        });
    }

    public static void DeleteTenantCookie(
        this ControllerBase controller, string name, bool httpOnly, IWebHostEnvironment env, string path = "/")
    {
        controller.Response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = path,
            Domain = IsPendingCookie(name) ? controller.GetPendingCookieDomain() : null
        });
    }

    public static AuthSessionResponseDto WithContinueUrl(
        this ControllerBase controller,
        LoginResponseDto dto,
        AuthSessionResponseDto response)
    {
        var path = dto.RequiresMfa
            ? "/api/v1/auth/mfa/verify"
            : dto.RequiresLegalAcceptance
                ? "/api/v1/legal/acceptances/complete-login"
                : dto.RequiresPasswordChange
                    ? "/api/v1/auth/force-change-password"
                    : null;

        if (path is null)
            return response;

        return response with
        {
            // Continue on the host where login started. Root-host login therefore remains on the
            // root host; tenant-host login remains tenant-host. Tenant identity comes only from
            // the opaque server-side challenge, never from the browser.
            ContinueUrl = $"{controller.Request.Scheme}://{controller.Request.Host.Value}{path}"
        };
    }

    private static string? GetPendingCookieDomain(this ControllerBase controller)
    {
        var services = controller.HttpContext?.RequestServices;
        if (services is null)
            return null;
        var configuration = services.GetRequiredService<IConfiguration>();
        var rootDomain = configuration["Tenancy:RootDomain"]?.Trim().TrimStart('.');
        return string.IsNullOrWhiteSpace(rootDomain) ? null : rootDomain;
    }

    private static bool IsPendingCookie(string name) =>
        name is "onevo_mfa" or "onevo_legal_pending" or "onevo_legal_csrf";
}
