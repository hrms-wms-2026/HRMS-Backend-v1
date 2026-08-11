using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.AccessGrantRequests;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

/// <summary>
/// Approve/reject actions on the pending AccessGrantRequest a position's "requires approval"
/// access template produces during employee onboarding finalization. TenantId is always
/// server-derived; both actions take the request id from the route only.
///
/// Permission: no permission finer than employees:write exists for position-access approval in
/// this codebase (the userflow doc references position:approve/org:manage, but neither is
/// seeded for this purpose - see PermissionSeeder.cs). Both actions are gated by employees:write
/// until a dedicated approval permission is introduced.
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

    /// <summary>Re-validates the draft/position/seat/role exactly as finalize would, then
    /// creates the pending user, employee, position assignment, checklist tasks, generated user
    /// role, and invitation (queued via outbox) in one transaction, and marks both the request
    /// and the draft decided.</summary>
    [HttpPost("{id:guid}/approve-and-send-invite")]
    [RequirePermission("employees:write")]
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
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectAccessGrantRequestRequest? request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RejectAccessGrantRequestCommand(id, request?.DecisionNote), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
