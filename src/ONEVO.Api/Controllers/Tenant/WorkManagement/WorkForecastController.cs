using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Features.TimeAttendance.Team.Queries;
using ONEVO.Application.Features.WorkManagement.Forecast.Queries.GetModuleForecast;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>My Team overtime forecast widgets (spec 2026-11-08 §4): remaining work against capacity x the 4-week
/// task rate. The module forecast is relationship-based like the rest of Work Management (module gate only, the
/// handler computes read access); the employee forecast is Team Status's population and needs attendance:read.</summary>
[ApiController]
[Route("api/v1/work/forecast")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public sealed class WorkForecastController(IMediator mediator) : ControllerBase
{
    /// <summary>Widget team-module-forecast: the readable, open Modules with their predicted hours and overtime.</summary>
    [HttpGet("modules")]
    public async Task<IActionResult> Modules(
        [FromQuery] string? status = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] string? sort = "overtime",
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetModuleForecastQuery(status, projectId, sort, search, page, pageSize), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Widget team-employee-forecast: each covered employee's worst deadline window at their task rate.</summary>
    [HttpGet("employees")]
    [RequirePermission(PermissionCatalog.Attendance.Read)]
    public async Task<IActionResult> Employees(
        [FromQuery] string? status = null,
        [FromQuery] string? sort = "overtime",
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTeamEmployeeForecastQuery(status, sort, search, page, pageSize), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
