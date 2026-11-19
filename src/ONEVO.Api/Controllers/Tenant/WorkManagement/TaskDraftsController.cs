using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.TaskDrafts;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.TaskDrafts;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>Owner-only "create task" drafts. Every operation is scoped to the caller; another
/// user's draft is indistinguishable from a missing one (404).</summary>
[ApiController]
[Route("api/v1/work/task-drafts")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public sealed class TaskDraftsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ToResult(await mediator.Send(new ListMyTaskDraftsQuery(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        ToResult(await mediator.Send(new GetTaskDraftQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveTaskDraftRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new SaveTaskDraftCommand(null, request.ProjectId, request.Title, request.Payload.GetRawText()), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveTaskDraftRequest request, CancellationToken ct) =>
        ToResult(await mediator.Send(
            new SaveTaskDraftCommand(id, request.ProjectId, request.Title, request.Payload.GetRawText()), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteTaskDraftCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    private IActionResult ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
