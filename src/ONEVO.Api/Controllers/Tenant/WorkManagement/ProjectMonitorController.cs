using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Monitoring.Commands.RefreshProjectMonitor;
using ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckModuleCapacity;
using ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckTaskLoad;
using ONEVO.Application.Features.WorkManagement.Monitoring.Queries.ListProjectMonitorAlerts;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

public sealed record ModuleCapacityCheckRequest(
    Guid? ModuleId, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, List<Guid>? MemberEmployeeIds);

public sealed record TaskLoadCheckRequest(
    Guid? TaskId, List<Guid>? AssigneeEmployeeIds, DateOnly? DueDate, decimal? EstimatedHours);

/// <summary>The project monitor: advisory planning checks for the Module and task forms (they never
/// block a save) and the open alerts the hourly monitor has found in the project tree.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class ProjectMonitorController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectMonitorController(IMediator mediator) => _mediator = mediator;

    [HttpPost("projects/{projectId:guid}/monitor/module-check")]
    public async Task<IActionResult> CheckModule(Guid projectId, [FromBody] ModuleCapacityCheckRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new CheckModuleCapacityQuery(
            projectId, body.ModuleId, body.StartDate, body.EndDate, body.AllocatedHours, body.MemberEmployeeIds), ct));

    [HttpPost("projects/{projectId:guid}/monitor/task-check")]
    public async Task<IActionResult> CheckTask(Guid projectId, [FromBody] TaskLoadCheckRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new CheckTaskLoadQuery(
            projectId, body.TaskId, body.AssigneeEmployeeIds ?? [], body.DueDate, body.EstimatedHours), ct));

    [HttpGet("projects/{projectId:guid}/monitor/alerts")]
    public async Task<IActionResult> Alerts(Guid projectId, CancellationToken ct)
        => ToResult(await _mediator.Send(new ListProjectMonitorAlertsQuery(projectId), ct));

    /// <summary>Re-checks the project now (instead of waiting for the hourly job) and returns the
    /// caller's open alerts - what the Tree calls when it loads, including right after a save.</summary>
    [HttpPost("projects/{projectId:guid}/monitor/refresh")]
    public async Task<IActionResult> Refresh(Guid projectId, CancellationToken ct)
    {
        var refreshed = await _mediator.Send(new RefreshProjectMonitorCommand(projectId), ct);
        if (!refreshed.IsSuccess)
            return Problem(refreshed.Error, statusCode: refreshed.StatusCode ?? 400);
        return ToResult(await _mediator.Send(new ListProjectMonitorAlertsQuery(projectId), ct));
    }

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
