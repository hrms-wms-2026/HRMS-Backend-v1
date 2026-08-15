using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/objectives")]
[Authorize(Policy = "TenantPolicy")]
public class ObjectivesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ObjectivesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a sub-milestone under an existing Objective. Caller must be the parent's current Head.</summary>
    [HttpPost]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create([FromBody] CreateObjectiveRequest request, CancellationToken ct)
    {
        var command = new CreateObjectiveCommand(
            request.ParentObjectiveId, request.Title, request.Description,
            request.StartDate, request.EndDate, request.AllocatedHours, request.HeadUserId);

        var result = await _mediator.Send(command, ct);

        // No CreatedAtAction: there is no single-Objective read route in this design (design §7 -
        // only the full-tree endpoint, GetTree, exists), so there is nothing real to point a
        // Location header at. StatusCode(201, ...) returns the created resource's body without
        // fabricating a link to a route that doesn't resolve.
        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Gets a single milestone by id. Permission-or-ancestor-membership, checked in-handler.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveByIdQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Edits a milestone. Non-conflicting edits apply immediately; edits that would conflict with the parent's date/hours constraints become a pending approval request unless the caller is the milestone's own creator. Frozen (400) once the milestone is Achieved.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditObjectiveRequest request, CancellationToken ct)
    {
        var command = new EditObjectiveCommand(id, request.Title, request.Description, request.StartDate, request.EndDate, request.AllocatedHours);
        var result = await _mediator.Send(command, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? Ok(result.Value.Objective!.ToViewModel())
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Soft-deletes a milestone. Applies immediately if the caller created it; otherwise creates a pending approval request routed to the milestone's Reporting Manager.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Reassigns a milestone's head. Same immediate-vs-pending split as Delete. On applying, also syncs project membership for both heads and cascades ReportingManagerId to direct children.</summary>
    [HttpPost("{id:guid}/transfer")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferObjectiveHeadRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransferObjectiveHeadCommand(id, request.NewHeadEmployeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Invites an employee to this milestone. Head-only. Immediate no-op (204) if already an active member; otherwise creates a pending invitation (202) the invited employee must accept.</summary>
    [HttpPost("{id:guid}/members")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddObjectiveMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddObjectiveMemberCommand(id, request.EmployeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.AlreadyMember
            ? StatusCode(204, result.Value.ToViewModel())
            : StatusCode(202, result.Value.ToViewModel());
    }

    /// <summary>Removes a member from this milestone. Head-only. Rejects removing the current head - use Transfer instead.</summary>
    [HttpDelete("{id:guid}/members/{employeeId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid employeeId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveObjectiveMemberCommand(id, employeeId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Marks a milestone Achieved. Requires every direct sub-milestone to already be Achieved. Same immediate-vs-pending split as Delete.</summary>
    [HttpPost("{id:guid}/achieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Reverts an Achieved milestone back to active. Same immediate-vs-pending split as Delete.</summary>
    [HttpPost("{id:guid}/unachieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Unachieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnachieveObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>An Objective's parent detail plus its full nested descendant subtree. Caller must be {id}'s current Head.</summary>
    [HttpGet("{id:guid}/tree")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetSubtree(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveSubtreeQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Approves a pending change request. Caller must be the request's Reporting Manager.</summary>
    [HttpPost("change-requests/{requestId:guid}/approve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ApproveChangeRequest(Guid requestId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveObjectiveChangeRequestCommand(requestId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Rejects a pending change request. Caller must be the request's Reporting Manager. The Objective is left unchanged.</summary>
    [HttpPost("change-requests/{requestId:guid}/reject")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> RejectChangeRequest(Guid requestId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectObjectiveChangeRequestCommand(requestId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The caller's own approval queue - pending requests where they are the Reporting Manager.</summary>
    [HttpGet("change-requests/mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ListMyChangeRequests(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMyObjectiveChangeRequestsQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(r => r.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Milestones the caller used to have active access to but no longer does (Transferred away, removed as a member, or Achieved with no other reason to stay in the project). Read-only.</summary>
    [HttpGet("mine/history")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> MyHistory(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyObjectiveHistoryQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(h => h.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The full Objective tree for a Project, scoped to what the caller can reach (design §5). No [RequirePermission] here on purpose: the handler checks membership fallback itself, matching GetProjectByIdQueryHandler's pattern.</summary>
    [HttpGet("~/api/v1/work/projects/{projectId:guid}/objectives")]
    public async Task<IActionResult> GetTree(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTreeQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(o => o.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Every milestone in this project the caller has ever had a project_members row for, any status - the frontend filters by objectiveIsActive/isAchieved/membershipIsActive as needed. Owner and Reporting Manager names are resolved server-side. No [RequirePermission] beyond the module base gate: this endpoint can only ever return the caller's own rows, so an unrelated projectId just yields an empty array, never 403/404.</summary>
    [HttpGet("~/api/v1/work/projects/{projectId:guid}/objectives/mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetMine(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyProjectMilestonesQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(m => m.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
