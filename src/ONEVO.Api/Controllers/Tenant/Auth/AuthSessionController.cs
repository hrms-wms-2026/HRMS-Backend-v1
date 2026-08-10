using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthSessionController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSessionExchangeService _tenantSessionExchange;

    public AuthSessionController(
        IMediator mediator,
        IWebHostEnvironment env,
        ITenantContext tenantContext,
        ITenantSessionExchangeService tenantSessionExchange)
    {
        _mediator = mediator;
        _env = env;
        _tenantContext = tenantContext;
        _tenantSessionExchange = tenantSessionExchange;
    }

    /// <summary>
    /// Consumes a one-time exchange code issued by a fully-authenticated base-domain login and
    /// creates the real, host-scoped tenant session. Tenant-host-only: the tenant is resolved from
    /// the request host by HostTenantResolutionMiddleware, never from the request body/headers.
    /// </summary>
    [HttpPost("session-exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> SessionExchange([FromBody] TenantSessionExchangeRequest request, CancellationToken ct)
    {
        if (_tenantContext.ContextMode != TenantContextMode.Tenant)
            return Problem("Session exchange is only available on a tenant host.", statusCode: 400);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _tenantSessionExchange.ConsumeAsync(
            request.Code, _tenantContext.TenantId, ip, ua, ct);

        return await this.HandleSessionResultAsync(result, _env);
    }

    /// <summary>Return safe metadata for the current tenant session.</summary>
    [HttpGet("me")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCurrentSessionQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 401);

        return Ok(result.Value!.ToViewModel());
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
        this.DeleteTenantCookie("onevo_legal_csrf", httpOnly: false, _env);
        return NoContent();
    }
}
