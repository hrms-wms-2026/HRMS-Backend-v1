using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;
using ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;
using ONEVO.Application.Features.Auth.Login.Commands.Login;
using ONEVO.Application.Features.Auth.Login.Commands.MfaEnable;
using ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;
using ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;
using ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;
using ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;
using ONEVO.Application.Features.Auth.Invite.Queries.GetInvitationByToken;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;
    public AuthController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    /// <summary>Public invitation preview (token from email link).</summary>
    [HttpGet("invitations/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInvitation(string token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetInvitationByTokenQuery(token), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 404);
        return Ok(result.Value);
    }

    /// <summary>Complete invitation by setting password (and optional phone).</summary>
    [HttpPost("invitations/{token}/accept-password")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvitationPassword(
        string token,
        [FromBody] AcceptInvitationPasswordRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new AcceptInvitationPasswordCommand(token, request.Password, request.Phone, ip, ua),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;
        if (dto.RequiresMfa)
        {
            SetMfaChallengeCookie(dto.MfaChallenge);
            return StatusCode(202, dto.ToSessionResponse());
        }

        if (dto.RequiresPasswordChange)
            return StatusCode(202, dto.ToSessionResponse());

        await SignInAsync(dto);
        return Ok(dto.ToSessionResponse());
    }

    /// <summary>Complete invitation with Google Sign-In.</summary>
    [HttpPost("invitations/{token}/accept-google")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvitationGoogle(
        string token,
        [FromBody] AcceptInvitationGoogleRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new AcceptInvitationGoogleCommand(token, request.GoogleIdToken, ip, ua),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;
        if (dto.RequiresMfa)
        {
            SetMfaChallengeCookie(dto.MfaChallenge);
            return StatusCode(202, dto.ToSessionResponse());
        }

        if (dto.RequiresPasswordChange)
            return StatusCode(202, dto.ToSessionResponse());

        await SignInAsync(dto);
        return Ok(dto.ToSessionResponse());
    }

    /// <summary>Login with email + password. Returns safe session metadata or an authentication challenge.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        ClearInvalidTenantSessionCookies();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new LoginCommand(request.Email, request.Password, ip, ua), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;

        if (dto.RequiresPasswordChange)
            return StatusCode(202, dto.ToSessionResponse());

        if (dto.RequiresMfa)
        {
            SetMfaChallengeCookie(dto.MfaChallenge);
            return StatusCode(202, dto.ToSessionResponse());
        }

        await SignInAsync(dto);
        return Ok(dto.ToSessionResponse());
    }

    /// <summary>Return safe metadata for the current tenant session.</summary>
    [HttpGet("me")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCurrentSessionQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);

        return Ok(result.Value);
    }

    /// <summary>Logout — revokes the server-side session.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("TenantScheme");
        DeleteTenantCookie("onevo_csrf", httpOnly: false);
        DeleteTenantCookie("onevo_mfa", httpOnly: true, path: "/api/v1/auth");
        return NoContent();
    }

    /// <summary>Begin MFA setup — returns TOTP secret + QR code URI.</summary>
    [HttpPost("mfa/enable")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> EnableMfa(CancellationToken ct)
    {
        var result = await _mediator.Send(new EnableMfaCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>Verify TOTP code to complete MFA challenge or finish MFA setup.</summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyMfa([FromBody] VerifyMfaRequest request, CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("onevo_mfa", out var mfaChallenge)
            || string.IsNullOrWhiteSpace(mfaChallenge))
        {
            return Problem("MFA challenge is missing or expired.", statusCode: 401);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new VerifyMfaCommand(mfaChallenge, request.Code, ip, ua), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);

        var dto = result.Value!;
        DeleteTenantCookie("onevo_mfa", httpOnly: true, path: "/api/v1/auth");
        await SignInAsync(dto);
        return Ok(dto.ToSessionResponse());
    }

    /// <summary>Request a password reset email.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _mediator.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Ok(new { message = "If the email exists, a reset link has been sent." });
    }

    /// <summary>Reset password using a valid reset token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(new { message = "Password reset successful. Please log in." });
    }

    /// <summary>Forced password change when must_change_password is true.</summary>
    [HttpPost("force-change-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForceChangePassword([FromBody] ForceChangePasswordRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new ForcePasswordChangeCommand(request.Email, request.CurrentPassword, request.NewPassword, ip, ua), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;
        await SignInAsync(dto);
        return Ok(dto.ToSessionResponse());
    }

    private async Task SignInAsync(ONEVO.Application.Features.Auth.Login.DTOs.Responses.LoginResponseDto dto)
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

        await HttpContext.SignInAsync("TenantScheme", principal, props);

        Response.Cookies.Append("onevo_csrf", dto.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = dto.ExpiresAt
        });
    }

    private void ClearInvalidTenantSessionCookies()
    {
        var hasSessionCookie = Request.Cookies.ContainsKey("onevo_session");
        var isAuthenticated = User.Identity?.IsAuthenticated == true;

        if (!hasSessionCookie || isAuthenticated)
            return;

        DeleteTenantCookie("onevo_session", httpOnly: true);
        DeleteTenantCookie("onevo_csrf", httpOnly: false);
    }

    private void SetMfaChallengeCookie(string? mfaChallenge)
    {
        if (string.IsNullOrWhiteSpace(mfaChallenge))
            throw new InvalidOperationException("MFA challenge material was not created.");

        Response.Cookies.Append("onevo_mfa", mfaChallenge, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        });
    }

    private void DeleteTenantCookie(string name, bool httpOnly, string path = "/")
    {
        Response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = path
        });
    }

    // ── Request shapes ────────────────────────────────────────────────────────

    public record LoginRequest(string Email, string Password);
    public record VerifyMfaRequest(string Code);
    public record ForgotPasswordRequest(string Email);
    public record ResetPasswordRequest(string Token, string NewPassword);
    public record ForceChangePasswordRequest(string Email, string CurrentPassword, string NewPassword);

    public record AcceptInvitationPasswordRequest(
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("phone")] string? Phone);

    public record AcceptInvitationGoogleRequest(
        [property: JsonPropertyName("google_id_token")] string GoogleIdToken);

}
