using MediatR;
using ONEVO.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Sprints;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.DeleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetSprintActivity;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSprintTasks;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class SprintsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SprintsController(IMediator mediator) => _mediator = mediator;

    /// <summary>201 with the sprint when the caller is at or above their creator position, otherwise 202 { approvalRequestId }.</summary>
    [HttpPost("projects/{projectId:guid}/sprints")]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateSprintRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new CreateSprintCommand(projectId, request.Name, request.Goal, request.TaskIds ?? Array.Empty<Guid>()), ct), appliedStatus: 201);

    [HttpPatch("sprints/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditSprintRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new EditSprintCommand(id, request.Name, request.Goal, request.StartDate, request.EndDate), ct));

    [HttpPost("sprints/{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, [FromBody] StartSprintRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new StartSprintCommand(id, request.StartDate, request.EndDate, request.Goal), ct));

    [HttpPost("sprints/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteSprintRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new CompleteSprintCommand(id, request.Disposition, request.TargetSprintId), ct));

    [HttpPost("sprints/{id:guid}/achieve")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new AchieveSprintCommand(id), ct));

    /// <summary>Only a Complete or Achieved sprint (else 409). 204 when deleted, 202 { approvalRequestId } when sent for approval.</summary>
    [HttpDelete("sprints/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new DeleteSprintCommand(id), ct));

    /// <summary>Applied → the sprint (or 204 after a delete); pending → 202 { approvalRequestId }.</summary>
    private IActionResult ToResult(Result<SprintWriteOutcome> result, int appliedStatus = 200)
        => !result.IsSuccess ? Problem(result.Error, statusCode: result.StatusCode ?? 400)
         : result.Value!.ApprovalRequestId is { } id ? StatusCode(202, new { approvalRequestId = id })
         : result.Value.Sprint is { } s ? StatusCode(appliedStatus, s.ToViewModel())
         : NoContent();

    [HttpPut("sprints/{id:guid}/tasks")]
    public async Task<IActionResult> SetTasks(Guid id, [FromBody] SetSprintTasksRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetSprintTasksCommand(id, request.AddTaskIds ?? Array.Empty<Guid>(), request.RemoveTaskIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("sprints/{id:guid}/tasks")]
    public async Task<IActionResult> GetTasks(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSprintTasksQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("sprints/{id:guid}/activity")]
    public async Task<IActionResult> GetActivity(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSprintActivityQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(a => a.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("projects/{projectId:guid}/sprints")]
    public async Task<IActionResult> GetByProject(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectSprintsQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("objectives/{objectiveId:guid}/sprints")]
    public async Task<IActionResult> GetByObjective(Guid objectiveId, [FromQuery] bool activeOnly, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveSprintsQuery(objectiveId, activeOnly), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
