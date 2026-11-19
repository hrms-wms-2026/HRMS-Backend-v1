using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class CommentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CommentsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> GetComments(Guid taskId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCommentsForTaskQuery(taskId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(c => c.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> PostComment(Guid taskId, [FromBody] CreateTaskCommentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateTaskCommentCommand(taskId, null, request.Content, request.AttachmentFileIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("comments/{id:guid}/replies")]
    public async Task<IActionResult> PostReply(Guid id, [FromBody] CreateTaskCommentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateTaskCommentCommand(null, id, request.Content, request.AttachmentFileIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("comments/{id:guid}")]
    public async Task<IActionResult> EditComment(Guid id, [FromBody] EditTaskCommentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new EditTaskCommentCommand(id, request.Content, request.AttachmentFileIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("comments/{id:guid}")]
    public async Task<IActionResult> DeleteComment(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTaskCommentCommand(id), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("comments/{id:guid}/reactions")]
    public async Task<IActionResult> AddReaction(Guid id, [FromBody] AddTaskCommentReactionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddTaskCommentReactionCommand(id, request.Emoji), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("comments/{id:guid}/reactions/{emoji}")]
    public async Task<IActionResult> RemoveReaction(Guid id, string emoji, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveTaskCommentReactionCommand(id, emoji), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
