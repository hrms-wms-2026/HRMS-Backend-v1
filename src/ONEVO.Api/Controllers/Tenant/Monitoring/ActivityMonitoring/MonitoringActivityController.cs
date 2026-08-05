using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailyRange;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivitySnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.ActivityMonitoring;

/// <summary>
/// HR/Manager query APIs for activity snapshots and daily summaries.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/activity")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringActivityController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringActivityController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// List normalized activity snapshots for an employee on a given date (UTC day).
    /// </summary>
    [HttpGet("snapshots")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetSnapshots(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetActivitySnapshotsQuery
        {
            EmployeeId = employeeId,
            Date = date,
            Page = page,
            PageSize = pageSize
        }, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Get pre-aggregated daily activity summary for an employee (null if not yet aggregated).
    /// </summary>
    [HttpGet("daily-summary")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetDailySummary(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetActivityDailySummaryQuery
        {
            EmployeeId = employeeId,
            Date = date
        }, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Get daily activity summaries for a date range (max 31 days).
    /// </summary>
    [HttpGet("daily-range")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetDailyRange(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetActivityDailyRangeQuery
        {
            EmployeeId = employeeId,
            From = from,
            To = to
        }, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}
