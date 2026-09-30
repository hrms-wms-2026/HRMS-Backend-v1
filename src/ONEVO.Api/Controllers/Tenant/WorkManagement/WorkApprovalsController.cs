using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetProjectApprovalFeed;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;
using ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

public sealed record ApproveWorkApprovalRequestRequest(string? EditedPayloadJson, string? Comment);
public sealed record RejectWorkApprovalRequestRequest(string? Comment);

/// <summary>The unified Work Management approval inbox and project notification history, backed by
/// the approval and notification engines.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class WorkApprovalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkApprovalsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("projects/{projectId:guid}/approvals")]
    public async Task<IActionResult> List(Guid projectId, [FromQuery] string scope = "inbox", [FromQuery] string? status = null, CancellationToken ct = default)
        => ToResult(await _mediator.Send(new ListProjectWorkApprovalsQuery(projectId, scope, status), ct));

    /// <summary>Every request the caller sent or received in this project (engine requests + module invitations), pending first.</summary>
    [HttpGet("projects/{projectId:guid}/approval-feed")]
    public async Task<IActionResult> Feed(Guid projectId, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetProjectApprovalFeedQuery(projectId), ct));

    /// <summary>The explanation card of one row: field diff (current / requested / applied), invitation detail, permissions.</summary>
    [HttpGet("approvals/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, [FromQuery] string source = "engine", CancellationToken ct = default)
        => ToResult(await _mediator.Send(new GetApprovalDetailQuery(id, source), ct));

    [HttpPost("approvals/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveWorkApprovalRequestRequest? body, CancellationToken ct)
        => ToResult(await _mediator.Send(new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Approve, body?.EditedPayloadJson, body?.Comment), ct));

    [HttpPost("approvals/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectWorkApprovalRequestRequest? body, CancellationToken ct)
        => ToResult(await _mediator.Send(new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Reject, null, body?.Comment), ct));

    [HttpPost("approvals/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Cancel, null, null), ct));

    [HttpGet("projects/{projectId:guid}/work-notifications")]
    public async Task<IActionResult> Notifications(Guid projectId, [FromQuery] int page = 1, CancellationToken ct = default)
        => ToResult(await _mediator.Send(new ListProjectWorkNotificationsQuery(projectId, page), ct));

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
