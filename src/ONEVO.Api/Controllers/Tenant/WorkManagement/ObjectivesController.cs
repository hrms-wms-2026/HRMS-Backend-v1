using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
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

    /// <summary>Edits a milestone. Non-conflicting edits apply immediately; edits that would conflict with the parent's date/hours constraints become a pending approval request unless the caller is the milestone's own creator.</summary>
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

    /// <summary>Reassigns a milestone's head. Same immediate-vs-pending split as Delete.</summary>
    [HttpPost("{id:guid}/transfer")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferObjectiveHeadRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransferObjectiveHeadCommand(id, request.NewHeadUserId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
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

    /// <summary>The full Objective tree for a Project. Caller needs an active membership somewhere in the project. No [RequirePermission] here on purpose: the handler checks projects:access-equivalent membership fallback itself, matching GetProjectByIdQueryHandler's pattern.</summary>
    [HttpGet("~/api/v1/work/projects/{projectId:guid}/objectives")]
    public async Task<IActionResult> GetTree(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTreeQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(o => o.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
