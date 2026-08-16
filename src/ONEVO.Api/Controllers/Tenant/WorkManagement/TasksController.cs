using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
public class TasksController : ControllerBase
{
    private readonly IMediator _mediator;

    public TasksController(IMediator mediator) => _mediator = mediator;

    [HttpPost("objectives/{objectiveId:guid}/tasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid objectiveId, [FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskCommand(
            objectiveId, request.Title, request.Description, request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("objectives/{objectiveId:guid}/tasks")]
    public async Task<IActionResult> GetByObjective(Guid objectiveId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTasksQuery(objectiveId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("objectives/{objectiveId:guid}/task-statuses")]
    public async Task<IActionResult> GetStatuses(Guid objectiveId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTaskStatusesQuery(objectiveId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("objectives/{objectiveId:guid}/task-statuses/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> EditStatus(Guid objectiveId, Guid id, [FromBody] EditTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskStatusCommand(id, request.Name, request.DisplayOrder, request.RequiresApproval, request.ApproverId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("tasks/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskCommand(id, request.Title, request.Description, request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("tasks/{id:guid}/status")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> MoveStatus(Guid id, [FromBody] MoveTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new MoveTaskStatusCommand(id, request.NewStatusId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("tasks/{id:guid}/assignments")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AssignTaskCommand(id, request.EmployeeId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("tasks/{id:guid}/assignments/{employeeId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Unassign(Guid id, Guid employeeId, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnassignTaskCommand(id, employeeId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
