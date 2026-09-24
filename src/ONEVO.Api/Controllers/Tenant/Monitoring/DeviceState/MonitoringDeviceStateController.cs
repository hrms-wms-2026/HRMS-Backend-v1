using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.DeviceState.Queries.GetDeviceStateSnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.DeviceState;

[ApiController]
[Route("api/v1/monitoring/device-state")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringDeviceStateController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringDeviceStateController(IMediator mediator) => _mediator = mediator;

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
            new GetDeviceStateSnapshotsQuery { EmployeeId = employeeId, Date = date, Page = page, PageSize = pageSize }, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
