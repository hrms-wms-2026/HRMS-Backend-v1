using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.Commands.AddLeavePolicyVersion;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.DeleteLeavePolicyVersion;
using ONEVO.Application.Features.Leave.Policy.Commands.SetLeavePolicyActive;
using ONEVO.Application.Features.Leave.Policy.Commands.SetLeavePolicyAssignments;
using ONEVO.Application.Features.Leave.Policy.Commands.UpdateLeavePolicyDetails;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/policies")]
[Authorize(Policy = "TenantPolicy")]
public class LeavePoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeavePoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new ListLeavePoliciesQuery(includeInactive), ct));

    [HttpGet("{leavePolicyId:guid}")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> Get(Guid leavePolicyId, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new GetLeavePolicyQuery(leavePolicyId), ct));

    [HttpPost]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Create([FromBody] CreateLeavePolicyRequest request, CancellationToken ct = default) =>
        ToWriteResult(await _mediator.Send(new CreateLeavePolicyCommand(
            request.LeaveTypeId, request.Name, request.Description, request.Rules.ToInput(),
            Targets(request.Assignments), request.ReplaceConflicts), ct));

    [HttpPut("{leavePolicyId:guid}")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> UpdateDetails(
        Guid leavePolicyId, [FromBody] UpdateLeavePolicyDetailsRequest request, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new UpdateLeavePolicyDetailsCommand(leavePolicyId, request.Name, request.Description), ct));

    [HttpPost("{leavePolicyId:guid}/versions")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> AddVersion(
        Guid leavePolicyId, [FromBody] AddLeavePolicyVersionRequest request, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new AddLeavePolicyVersionCommand(leavePolicyId, request.ApplyFrom, request.Rules.ToInput()), ct));

    [HttpDelete("{leavePolicyId:guid}/versions/{versionId:guid}")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> DeleteVersion(Guid leavePolicyId, Guid versionId, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new DeleteLeavePolicyVersionCommand(leavePolicyId, versionId), ct));

    [HttpPut("{leavePolicyId:guid}/assignments")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> SetAssignments(
        Guid leavePolicyId, [FromBody] SetLeavePolicyAssignmentsRequest request, CancellationToken ct = default) =>
        ToWriteResult(await _mediator.Send(new SetLeavePolicyAssignmentsCommand(
            leavePolicyId, Targets(request.Assignments), request.ReplaceConflicts), ct));

    [HttpPost("{leavePolicyId:guid}/activate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Activate(Guid leavePolicyId, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new SetLeavePolicyActiveCommand(leavePolicyId, true), ct));

    [HttpPost("{leavePolicyId:guid}/deactivate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Deactivate(Guid leavePolicyId, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new SetLeavePolicyActiveCommand(leavePolicyId, false), ct));

    [HttpPost("{leavePolicyId:guid}/clone")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Clone(
        Guid leavePolicyId, [FromBody] CloneLeavePolicyRequest request, CancellationToken ct = default) =>
        ToResult(await _mediator.Send(new CloneLeavePolicyCommand(leavePolicyId, request.Name), ct));

    private static IReadOnlyList<LeavePolicyAssignmentInput> Targets(IReadOnlyList<LeavePolicyAssignmentRequest>? targets) =>
        (targets ?? []).Select(t => new LeavePolicyAssignmentInput(t.TargetKind, t.TargetId)).ToList();

    /// <summary>A write that needs confirmation answers 409 with the conflicting targets; the client
    /// shows them and resends with replaceConflicts = true.</summary>
    private IActionResult ToWriteResult(Result<LeavePolicyWriteResult> result)
    {
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        if (result.Value!.Conflicts.Count > 0)
            return Conflict(new { code = LeavePolicyWriteResult.ConflictCode, conflicts = result.Value.Conflicts });
        return Ok(new { id = result.Value.PolicyId });
    }

    private IActionResult ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);

    private IActionResult ToResult(Result result)
    {
        if (result.IsSuccess)
            return NoContent();
        if (result.ErrorCode is { } code)
            return StatusCode(result.StatusCode ?? 400, new { code, detail = result.Error });
        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
