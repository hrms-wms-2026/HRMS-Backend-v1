using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.AppUsage.Queries.GetAppUsageSnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.AppUsage;

/// <summary>
/// HR/Manager query API for app-usage snapshots. Sits alongside
/// MonitoringAppUsageIngestController on the same route prefix, split by
/// HTTP verb and by authorization policy (TenantPolicy vs TrayDevicePolicy).
/// </summary>
[ApiController]
[Route("api/v1/monitoring/app-usage")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringAppUsageController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringAppUsageController(IMediator mediator) => _mediator = mediator;

    [HttpGet("snapshots")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetSnapshots(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetAppUsageSnapshotsQuery { EmployeeId = employeeId, Date = date, Page = page, PageSize = pageSize }, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
