using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthSessionController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthSessionController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
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

    /// <summary>Logout - revokes the server-side session.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("TenantScheme");
        this.DeleteTenantCookie("onevo_csrf", httpOnly: false, _env);
        this.DeleteTenantCookie("onevo_mfa", httpOnly: true, _env, path: "/api/v1/auth/mfa/verify");
        this.DeleteTenantCookie("onevo_legal_pending", httpOnly: true, _env, path: "/api/v1/legal/acceptances/complete-login");
        this.DeleteTenantCookie("onevo_legal_csrf", httpOnly: false, _env, path: "/api/v1/legal/acceptances/complete-login");
        return NoContent();
    }
}
