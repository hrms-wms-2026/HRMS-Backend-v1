using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.AccessGrantRequests;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListOnboardingAccessGrantRequests;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

/// <summary>
/// List/approve/reject actions on the AccessGrantRequest a position's "requires approval"
/// access template produces during employee onboarding finalization. TenantId is always
/// server-derived; approve/reject take the request id from the route only, and the list action
/// never accepts tenantId as a query parameter.
///
/// Permission: list remains employees:write. Approve and reject are gated by roles:manage.
/// </summary>
[ApiController]
[Route("api/v1/onboarding/access-grant-requests")]
[Authorize(Policy = "TenantPolicy")]
public class AccessGrantRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccessGrantRequestsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Position Approver Inbox: paged, tenant-scoped list of onboarding position-access
    /// requests. Defaults to pending onboarding requests. status/actionType are validated and
    /// mapped to their stored literal values; an unrecognized value is a 400, not a silent
    /// fallback.</summary>
    [HttpGet]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> List(
        [FromQuery] string? status = "pending",
        [FromQuery] string? actionType = "onboarding",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] Guid? legalEntityId = null,
        [FromQuery] Guid? requestedRoleId = null,
        CancellationToken ct = default)
    {
        var query = new ListOnboardingAccessGrantRequestsQuery(
            status, actionType, page, pageSize, search, legalEntityId, requestedRoleId);
        var result = await _mediator.Send(query, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("pending-for-me")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> PendingForMe(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPendingAccessGrantRequestsForMeQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Re-validates the draft/position/seat/role exactly as finalize would, then
    /// creates the pending user, employee, position assignment, checklist tasks, generated user
    /// role, and invitation (queued via outbox) in one transaction, and marks both the request
    /// and the draft decided.</summary>
    [HttpPost("{id:guid}/approve-and-send-invite")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> ApproveAndSendInvite(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ApproveAccessGrantRequestCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Marks the request rejected. Creates nothing and does not delete the request or
    /// the draft. When the draft was waiting on this decision, it moves back to
    /// draft/position_approval_rejected so HR can edit it, change position/access, or finalize
    /// again (which submits a fresh request); the rejection itself remains visible on the
    /// request's own status.</summary>
    [HttpPost("{id:guid}/reject")]
    [RequirePermission("roles:manage")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectAccessGrantRequestRequest? request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RejectAccessGrantRequestCommand(id, request?.DecisionNote), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
