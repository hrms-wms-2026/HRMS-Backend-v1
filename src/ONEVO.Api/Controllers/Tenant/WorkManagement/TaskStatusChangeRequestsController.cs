using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatusChangeRequests;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>Requests from project members to change the project's task-status template. Deciding
/// (approve/reject/cancel) goes through the unified WorkApprovalsController; only the project's
/// top (default) module owner can approve.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class TaskStatusChangeRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TaskStatusChangeRequestsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("projects/{projectId:guid}/task-status-change-requests")]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectTaskStatusChangeRequestsQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    // The handler admits exactly the owners/members allowed by the contextual request rules.
    [HttpPost("projects/{projectId:guid}/task-status-change-requests")]
    public async Task<IActionResult> Create(
        Guid projectId, [FromBody] CreateTaskStatusChangeRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateTaskStatusChangeRequestCommand(projectId, request.Note, request.Changes), ct);

        return result.IsSuccess
            ? StatusCode(202, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
