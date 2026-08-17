using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskCreationRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskCreationRequests;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetWorkNotificationNavigation;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
public class TasksController : ControllerBase
{
    private readonly IMediator _mediator;

    public TasksController(IMediator mediator) => _mediator = mediator;

    [HttpGet("my-deadlines")]
    public async Task<IActionResult> MyDeadlines([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyDeadlinesQuery(from, to), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Resolves Work Management notification click-through targets (Board / Approvals).</summary>
    [HttpGet("notification-navigation")]
    public async Task<IActionResult> NotificationNavigation(
        [FromQuery] string relatedEntityType, [FromQuery] Guid relatedEntityId, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetWorkNotificationNavigationQuery(relatedEntityType, relatedEntityId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("objectives/{objectiveId:guid}/tasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid objectiveId, [FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskCommand(
            objectiveId, request.Title, request.Description, request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId), ct);

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

    [HttpPost("objectives/{objectiveId:guid}/task-statuses")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CreateStatus(Guid objectiveId, [FromBody] CreateTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskStatusCommand(
            objectiveId, request.Name, request.DisplayOrder, request.Visibility, request.MarksTaskComplete,
            request.RequiresApproval, request.ApproverId), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("objectives/{objectiveId:guid}/task-statuses/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> EditStatus(Guid objectiveId, Guid id, [FromBody] EditTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskStatusCommand(
            id, request.Name, request.DisplayOrder, request.RequiresApproval, request.ApproverId, request.Visibility), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("task-statuses/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> DeleteStatus(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTaskStatusCommand(id), ct);

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

    [HttpPost("objectives/{objectiveId:guid}/task-creation-requests")]
    public async Task<IActionResult> CreateRequest(Guid objectiveId, [FromBody] CreateTaskCreationRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskCreationRequestCommand(
            objectiveId, request.Title, request.Description, request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId), ct);

        return result.IsSuccess
            ? StatusCode(202, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("task-creation-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveRequest(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveTaskCreationRequestCommand(id), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("task-creation-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectRequest(Guid id, [FromBody] RejectTaskCreationRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectTaskCreationRequestCommand(id, request.Comment), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("task-creation-requests/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRequest(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelTaskCreationRequestCommand(id), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("task-creation-requests/mine")]
    public async Task<IActionResult> MyRequests(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyTaskCreationRequestsQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(r => r.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
