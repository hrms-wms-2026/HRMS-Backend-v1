using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;
using ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;
using ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthPasswordController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthPasswordController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
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
    public async Task<IActionResult> ForceChangePassword([FromBody] ForcePasswordChangeRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new ForcePasswordChangeCommand(request.Email, request.CurrentPassword, request.NewPassword, ip, ua), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return await this.HandleSessionResultAsync(result, _env);
    }
}
