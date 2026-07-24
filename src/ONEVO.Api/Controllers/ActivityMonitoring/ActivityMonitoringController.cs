using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;
using System.Security.Claims;

namespace ONEVO.Api.Controllers.ActivityMonitoring;

[ApiController]
[Route("api/v1/activity")]
[Authorize]
public class ActivityMonitoringController : ControllerBase
{
    private readonly IMediator _mediator;
    public ActivityMonitoringController(IMediator mediator) => _mediator = mediator;

    // ── Manager-facing ─────────────────────────────────────────────────────────

    [HttpGet("summary/{employeeId}")]
    public async Task<IActionResult> GetDailySummary(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDailySummaryQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("snapshots/{employeeId}")]
    public async Task<IActionResult> GetSnapshots(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSnapshotsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("apps/{employeeId}")]
    public async Task<IActionResult> GetAppUsage(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAppUsageQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("meetings/{employeeId}")]
    public async Task<IActionResult> GetMeetings(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMeetingsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    // ── Self-service ───────────────────────────────────────────────────────────

    [HttpGet("my/summary")]
    public async Task<IActionResult> GetMySummary([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = GetCallerEmployeeId();
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetDailySummaryQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("my/apps")]
    public async Task<IActionResult> GetMyAppUsage([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = GetCallerEmployeeId();
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetAppUsageQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("my/meetings")]
    public async Task<IActionResult> GetMyMeetings([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = GetCallerEmployeeId();
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetMeetingsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    private Guid GetCallerEmployeeId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
