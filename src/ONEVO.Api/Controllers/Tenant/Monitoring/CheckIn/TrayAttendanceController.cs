namespace ONEVO.Api.Controllers.Tenant.Monitoring.CheckIn;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockOut;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;

/// <summary>
/// Tray-authenticated attendance actions. Authorization: Bearer {tray_access_token}.
/// Identity comes only from the tray JWT — never from query or body, matching
/// TrayMonitoringPolicyController.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray")]
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class TrayAttendanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayAttendanceController(IMediator mediator) => _mediator = mediator;

    [HttpGet("attendance-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAttendanceStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTrayAttendanceStatusQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpPost("clock-in")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ClockIn(CancellationToken ct)
    {
        var result = await _mediator.Send(new TrayClockInCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpPost("clock-out")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClockOut(CancellationToken ct)
    {
        var result = await _mediator.Send(new TrayClockOutCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
