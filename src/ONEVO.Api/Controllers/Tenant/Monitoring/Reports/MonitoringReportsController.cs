using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Reports.Queries.GetProductivityReport;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Reports;

[ApiController]
[Route("api/v1/monitoring/reports")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringReportsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// scope=employee requires scopeId=&lt;employeeId&gt;, scope=department requires
    /// scopeId=&lt;departmentId&gt;, scope=tenant ignores scopeId. "Daily/weekly/monthly"
    /// is just the caller choosing a from/to range - there is no separate endpoint per period.
    /// </summary>
    [HttpGet("productivity")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> GetProductivityReport(
        [FromQuery] ProductivityReportScope scope,
        [FromQuery] Guid? scopeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetProductivityReportQuery { Scope = scope, ScopeId = scopeId, From = from, To = to }, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
