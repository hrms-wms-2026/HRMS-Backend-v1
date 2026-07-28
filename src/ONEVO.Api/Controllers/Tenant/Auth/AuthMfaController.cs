using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Features.Auth.Login.Commands.MfaConfirmSetup;
using ONEVO.Application.Features.Auth.Login.Commands.MfaEnable;
using ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthMfaController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthMfaController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    /// <summary>Begin MFA setup - returns TOTP secret + QR code URI.</summary>
    [HttpPost("mfa/enable")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> EnableMfa(CancellationToken ct)
    {
        var result = await _mediator.Send(new EnableMfaCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>Verify TOTP code for a login MFA challenge.</summary>
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

        this.DeleteTenantCookie("onevo_mfa", httpOnly: true, _env, path: "/api/v1/auth/mfa/verify");
        return await this.HandleSessionResultAsync(result, _env);
    }

    /// <summary>Confirm first-time TOTP setup for the current authenticated user.</summary>
    [HttpPost("mfa/confirm-setup")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> ConfirmMfaSetup([FromBody] ConfirmMfaSetupRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmMfaSetupCommand(request.Code), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(new { success = true });
    }
}
