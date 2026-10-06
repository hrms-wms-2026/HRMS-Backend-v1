using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>
/// Applies an approved sprint.create as the requester (they become the sprint's creator), at the
/// creator position recorded on the request. Stores the new sprint's id as the request's target.
/// </summary>
public sealed class SprintCreateApplier : IApprovalActionApplier
{
    private readonly ISprintWriteService _writes;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IObjectiveRepository _objectives;

    public SprintCreateApplier(ISprintWriteService writes, IMilestoneMembershipCoordinator membership, IObjectiveRepository objectives)
    {
        _writes = writes;
        _membership = membership;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.SprintCreate;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var requester = await _membership.GetActiveAssigneeAsync(request.TenantId, request.RequestedByEmployeeId, ct);
        if (requester is null)
            return ApplyOutcome.Stale;

        SprintCreateInput? input;
        try { input = JsonSerializer.Deserialize<SprintCreateInput>(context.PayloadJson, SprintPayloadJson.Options); }
        catch (JsonException) { input = null; }
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
            return ApplyOutcome.Invalid("The sprint request has no name.");

        var position = request.PositionObjectiveId
            ?? (await _objectives.GetDefaultByProjectIdAsync(request.TenantId, request.ProjectId, ct))?.Id;
        if (position is null)
            return ApplyOutcome.Stale;

        var created = await _writes.CreateAsync(request.TenantId, requester.UserId, request.RequestedByEmployeeId,
            input with { TaskIds = input.TaskIds ?? Array.Empty<Guid>() }, position.Value, ct);
        if (!created.IsSuccess)
            return ApplyOutcome.Invalid(created.Error ?? "The sprint could not be created.");

        request.TargetId = created.Value!.Id;
        return ApplyOutcome.Applied();
    }
}
