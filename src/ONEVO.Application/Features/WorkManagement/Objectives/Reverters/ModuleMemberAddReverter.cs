using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.member_add by removing the member - but refuses (Conflict) if
/// they've since been assigned to any task in the Module, so a revert can never silently strand an
/// assignee with no membership behind it.</summary>
public sealed class ModuleMemberAddReverter : IApprovalActionReverter
{
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssignmentRepository _assignments;

    public ModuleMemberAddReverter(IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, ITaskAssignmentRepository assignments)
    {
        _objectives = objectives;
        _membership = membership;
        _assignments = assignments;
    }

    public string ActionType => WorkActionTypes.ModuleMemberAdd;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var input = JsonSerializer.Deserialize<ModuleMemberAddInput>(request.PayloadJson, ModulePayloadJson.Options);
        if (input is null || input.EmployeeId == Guid.Empty)
            return RevertOutcome.NotRevertable("The original request has no employee to remove.");

        if (await _assignments.AnyForEmployeeInObjectiveAsync(request.TenantId, module.Id, input.EmployeeId, ct))
            return RevertOutcome.Conflict("This member has already been assigned work in this milestone - remove them manually instead.");

        var result = await _membership.ApplyMemberRemoveAsync(request.TenantId, module, input.EmployeeId, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This member could not be removed.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
