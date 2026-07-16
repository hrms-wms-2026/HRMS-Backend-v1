using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity;
using ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;
using ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

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
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new AdminLoginCommand(request.Email, request.Password, ip, ua), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);

        await SignInAsync(result.Value!);
        return Ok(result.Value.ToSessionResponse());
    }

    /// <summary>Logout — revokes the server-side admin session.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("AdminScheme");
        Response.Cookies.Delete("admin_csrf");
        return NoContent();
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
