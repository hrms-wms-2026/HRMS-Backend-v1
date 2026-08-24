using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.DailyReport.Queries.ExportEmployeeDailyReport;
using ONEVO.Application.Features.Monitoring.DailyReport.Queries.GetEmployeeDailyReport;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.DailyReport;

/// <summary>
/// HR/Manager API combining activity, clock-in/out + break, and screenshot data
/// for one employee on one day, viewable as JSON or downloadable as .xlsx.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/daily-report")]
[Authorize(Policy = "TenantPolicy")]
public class MonitoringDailyReportController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringDailyReportController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>Combined daily report as JSON, including live 15-minute screenshot URLs.</summary>
    [HttpGet]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> Get(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetEmployeeDailyReportQuery { EmployeeId = employeeId, Date = date }, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>Same report as a downloadable .xlsx (Summary / Top Apps / Screenshots sheets).</summary>
    [HttpGet("export")]
    [RequirePermission("monitoring:read")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ExportEmployeeDailyReportQuery { EmployeeId = employeeId, Date = date }, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
