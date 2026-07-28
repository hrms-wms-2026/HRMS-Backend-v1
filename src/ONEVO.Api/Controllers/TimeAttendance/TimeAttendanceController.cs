using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.Queries.GetClockInContext;
using ONEVO.Application.Features.TimeAttendance.Queries.GetCurrentPresence;

namespace ONEVO.Api.Controllers.TimeAttendance;

[ApiController]
[Route("api/v1/time-attendance")]
public sealed class TimeAttendanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public TimeAttendanceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("clock-in/context")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> GetClockInContext(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new GetClockInContextQuery(agentId),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var context = result.Value!;
        return Ok(new
        {
            work_date = context.WorkDate,
            timezone = context.Timezone,
            schedule = new
            {
                id = context.WorkScheduleId,
                name = context.WorkScheduleName,
                start = context.ScheduledStart,
                end = context.ScheduledEnd,
                required_work_minutes = context.RequiredWorkMinutes
            },
            expected_work_area = context.ExpectedWorkArea,
            work_area_source = context.WorkAreaSource,
            location_required = context.LocationRequired,
            photo_required = context.PhotoRequired,
            reference_ready = context.ReferenceReady,
            remote_location_ready = context.RemoteProfileReady,
            is_holiday = context.IsHoliday,
            is_working_day = context.IsWorkingDay,
            is_clocked_in = context.IsClockedIn,
            can_clock_in = context.CanClockIn,
            reason_code = context.ReasonCode,
            monitoring_hard_stop_at = context.MonitoringHardStopAt
        });
    }

    [HttpPost("clock-in")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> ClockIn(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] ClockInRequest request,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var capture = request.Location is null
            ? null
            : new LocationCapture(
                request.Location.Latitude,
                request.Location.Longitude,
                request.Location.AccuracyMeters,
                request.Location.CapturedAt,
                request.Location.PermissionState);
        var result = await _mediator.Send(new ClockInCommand(
            AgentId: agentId,
            IdempotencyKey: idempotencyKey ?? string.Empty,
            Capture: capture,
            LocalNetworkClass: request.LocalNetworkClass,
            WifiBssidHash: request.WifiBssidHash,
            GatewayMacHash: request.GatewayMacHash,
            VpnDetected: request.VpnDetected,
            VerificationRecordId: request.VerificationRecordId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return StatusCode(result.Value!.HttpStatusCode, result.Value);
    }

    [HttpGet("presence/current")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> GetCurrentPresence(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new GetCurrentPresenceQuery(agentId),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    [HttpPost("clock-out")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> ClockOut(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new ClockOutCommand(agentId, idempotencyKey ?? string.Empty),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return StatusCode(result.Value!.HttpStatusCode, result.Value);
    }

    [HttpPost("breaks/start")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> StartBreak(
        [FromBody] StartBreakRequest request,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new StartBreakCommand(agentId, request.BreakType),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    [HttpPost("breaks/end")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> EndBreak(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new EndBreakCommand(agentId),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    private Guid GetAgentId()
    {
        var value = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public sealed record ClockInRequest(
        LocationCaptureRequest? Location,
        string? LocalNetworkClass,
        string? WifiBssidHash,
        string? GatewayMacHash,
        bool VpnDetected,
        Guid? VerificationRecordId);

    public sealed record LocationCaptureRequest(
        decimal Latitude,
        decimal Longitude,
        decimal AccuracyMeters,
        DateTimeOffset CapturedAt,
        string PermissionState);

    public sealed record StartBreakRequest(string BreakType);
}
