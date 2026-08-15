using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Dashboard.Queries.GetMonitoringDashboard;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Dashboard;

[ApiController]
[Route("api/v1/monitoring/dashboard")]
[Authorize(Policy = "TenantPolicy")]
public sealed class MonitoringDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringDashboardController(IMediator mediator)
        => _mediator = mediator;

    [HttpGet]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly date,
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? legalEntityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMonitoringDashboardQuery(
            Date: date,
            Search: search,
            DepartmentId: departmentId,
            LegalEntityId: legalEntityId,
            Page: page,
            PageSize: pageSize), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}
