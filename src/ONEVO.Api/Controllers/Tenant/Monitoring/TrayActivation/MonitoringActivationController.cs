using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.GenerateActivationCode;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RevokeDevice;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.TrayActivation;

[ApiController]
[Route("api/v1/monitoring/activation")]
public class MonitoringActivationController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringActivationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Generate a one-time activation code for the tray app.
    /// Called from the web portal by an authenticated employee.
    /// </summary>
    [HttpPost("generate")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> Generate(CancellationToken ct)
    {
        var result = await _mediator.Send(new GenerateActivationCodeCommand(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Exchange a one-time activation code for a JWT access token and refresh token.
    /// Called by the tray app — no authentication required, rate limited.
    /// </summary>
    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ExchangeActivationCodeCommand(
            request.Code,
            request.DeviceName,
            request.DeviceOs,
            request.DeviceFingerprint), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Refresh an expired access token using a valid refresh token.
    /// Called by the tray app — no authentication required, rate limited.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshTrayTokenCommand(
            request.RefreshToken,
            request.DeviceFingerprint), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Revoke the calling device's session and deactivate it. The device id is
    /// read from the authenticated bearer token, never from the request body.
    /// Called by the tray app on sign-out.
    /// </summary>
    [HttpPost("revoke")]
    [Authorize(Policy = "TrayDevicePolicy")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeDeviceCommand(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return NoContent();
    }

    public record ExchangeRequest(
        string Code,
        string DeviceName,
        string DeviceOs,
        string DeviceFingerprint);

    public record RefreshRequest(
        string RefreshToken,
        string DeviceFingerprint);
}
