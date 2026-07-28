using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.Access;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetEmployeeDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;
using ONEVO.Application.Features.Users.RepositoryInterfaces;

namespace ONEVO.Api.Controllers.ActivityMonitoring;

[ApiController]
[Route("api/v1/activity")]
[Authorize(Policy = "TenantPolicy")]
public class ActivityMonitoringController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly IUserProfileRepository _profiles;
    private readonly IMonitoringAccessResolver _access;

    public ActivityMonitoringController(
        IMediator mediator,
        ICurrentUser currentUser,
        IUserProfileRepository profiles,
        IMonitoringAccessResolver access)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _profiles = profiles;
        _access = access;
    }

    // ── Manager-facing ─────────────────────────────────────────────────────────

    [HttpGet("summary/{employeeId}")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetDailySummary(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (!await _access.CanReadEmployeeAsync(employeeId, ct))
            return Forbid();

        var result = await _mediator.Send(new GetDailySummaryQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("snapshots/{employeeId}")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetSnapshots(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (!await _access.CanReadEmployeeAsync(employeeId, ct))
            return Forbid();

        var result = await _mediator.Send(new GetSnapshotsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("apps/{employeeId}")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetAppUsage(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (!await _access.CanReadEmployeeAsync(employeeId, ct))
            return Forbid();

        var result = await _mediator.Send(new GetAppUsageQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("meetings/{employeeId}")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetMeetings(
        Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (!await _access.CanReadEmployeeAsync(employeeId, ct))
            return Forbid();

        var result = await _mediator.Send(new GetMeetingsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    // ── Self-service ───────────────────────────────────────────────────────────

    [HttpGet("my/summary")]
    [RequirePermission("activity:read:self")]
    public async Task<IActionResult> GetMySummary([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = await GetCallerEmployeeIdAsync(ct);
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetEmployeeDailySummaryQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("my/apps")]
    [RequirePermission("activity:read:self")]
    public async Task<IActionResult> GetMyAppUsage([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = await GetCallerEmployeeIdAsync(ct);
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetAppUsageQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("my/meetings")]
    [RequirePermission("activity:read:self")]
    public async Task<IActionResult> GetMyMeetings([FromQuery] DateOnly date, CancellationToken ct)
    {
        var employeeId = await GetCallerEmployeeIdAsync(ct);
        if (employeeId == Guid.Empty) return Unauthorized();
        var result = await _mediator.Send(new GetMeetingsQuery(employeeId, date), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    private async Task<Guid> GetCallerEmployeeIdAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty)
            return Guid.Empty;

        var employee = await _profiles.GetEmployeeByUserIdAsync(
            _currentUser.UserId,
            ct);
        return employee?.Id ?? Guid.Empty;
    }
}
