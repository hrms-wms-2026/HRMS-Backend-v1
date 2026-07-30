using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;
using ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;
using ONEVO.Application.Features.Auth.Login.Commands.AdminMfaConfirmSetup;
using ONEVO.Application.Features.Auth.Login.Commands.AdminMfaEnable;
using ONEVO.Application.Features.Auth.Login.Commands.AdminMfaVerify;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.Queries.GetAdminSessionContext;
using ONEVO.Application.Features.Auth.Login.Queries.GetAdminGoogleSsoConfig;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

[ApiController]
[Route("admin/v1/auth")]
public sealed class AdminAuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AdminAuthController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    /// <summary>
    /// Public Google SSO config for the admin login page. Anonymous because the login
    /// page needs it before the admin authenticates. Never returns secret material -
    /// only whether Google SSO is currently usable and its public clientId.
    /// </summary>
    [HttpGet("google-config")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleConfig(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAdminGoogleSsoConfigQuery(), ct);
        return Ok(result.Value);
    }

    /// <summary>Exchange a Google ID token for a cookie-backed platform-admin session.</summary>
    [HttpPost("google-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback([FromBody] AdminGoogleCallbackRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(new AdminGoogleLoginCommand(request.GoogleIdToken, ip, ua), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        await SignInAsync(result.Value!);
        return Ok(result.Value.ToSessionResponse());
    }

    /// <summary>
    /// Database-backed email + password login for Developer Platform users.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        ClearStaleAdminSessionCookies();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new AdminLoginCommand(request.Email, request.Password, ip, ua), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);

        var dto = result.Value!;

        if (dto.RequiresMfa)
        {
            SetAdminMfaChallengeCookie(dto.MfaSessionToken!);
            return StatusCode(202, dto.ToSessionResponse());
        }

        await SignInAsync(dto);
        return Ok(dto.ToSessionResponse());
    }

    /// <summary>Begin MFA setup for the calling admin - returns TOTP secret + QR code URI.</summary>
    [HttpPost("mfa/enable")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> EnableMfa(CancellationToken ct)
    {
        var result = await _mediator.Send(new EnableAdminMfaCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>Confirms a freshly generated TOTP secret and turns MFA on for this admin.</summary>
    [HttpPost("mfa/confirm-setup")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> ConfirmMfaSetup([FromBody] ConfirmAdminMfaSetupRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmAdminMfaSetupCommand(request.Code), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(new { success = true });
    }

    /// <summary>Return safe metadata for the current admin session (used to restore state on page refresh).</summary>
    [HttpGet("me")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAdminSessionContextQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);
        return Ok(result.Value);
    }

    /// <summary>Login step 2 - completes an MFA-gated admin login using the admin_mfa challenge cookie.</summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyMfa([FromBody] VerifyAdminMfaRequest request, CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("admin_mfa", out var challenge) || string.IsNullOrWhiteSpace(challenge))
            return Problem("MFA challenge is missing or expired.", statusCode: 401);

        var result = await _mediator.Send(new VerifyAdminMfaCommand(challenge, request.Code), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);

        Response.Cookies.Delete("admin_mfa", new CookieOptions { Path = "/admin/v1/auth" });
        await SignInAsync(result.Value!);
        return Ok(result.Value!.ToSessionResponse());
    }

    /// <summary>Logout - revokes the server-side admin session.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("AdminScheme");
        Response.Cookies.Delete("admin_csrf");
        return NoContent();
    }

    /// <summary>
    /// Deletes a leftover admin_session/admin_csrf pair before a fresh login attempt.
    /// CsrfProtectionMiddleware only checks for the presence of admin_session to decide whether
    /// CSRF must be validated - a stale cookie from an earlier session (still within its sliding
    /// window) makes it wrongly demand a CSRF token on pre-auth calls like mfa/verify.
    /// </summary>
    private void ClearStaleAdminSessionCookies()
    {
        if (!Request.Cookies.ContainsKey("admin_session"))
            return;

        Response.Cookies.Delete("admin_session", new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
        });
        Response.Cookies.Delete("admin_csrf", new CookieOptions
        {
            HttpOnly = false,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
        });
    }

    private void SetAdminMfaChallengeCookie(string challenge)
    {
        Response.Cookies.Append("admin_mfa", challenge, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/admin/v1/auth",
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        });
    }

    private async Task SignInAsync(AdminLoginResultDto dto)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, dto.PlatformUserId.ToString()),
            new(ClaimTypes.Email, dto.Email),
            new("platform_role", dto.PlatformRole)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));

        var props = new AuthenticationProperties
        {
            ExpiresUtc = dto.ExpiresAt
        };
        props.Items[AdminDatabaseTicketStore.PlatformRoleItemKey] = dto.PlatformRole;
        props.Items[AdminDatabaseTicketStore.CsrfTokenHashItemKey] = dto.CsrfTokenHash;

        await HttpContext.SignInAsync("AdminScheme", principal, props);

        Response.Cookies.Append("admin_csrf", dto.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Expires = dto.ExpiresAt
        });
    }
}
