using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.member_remove by re-inviting the employee, reusing
/// IMilestoneMembershipCoordinator.ApplyMemberAddAsync exactly as a normal member_add would.</summary>
public sealed class ModuleMemberRemoveReverter : IApprovalActionReverter
{
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;

    public ModuleMemberRemoveReverter(IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership)
    {
        _objectives = objectives;
        _membership = membership;
    }

    public string ActionType => WorkActionTypes.ModuleMemberRemove;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var input = JsonSerializer.Deserialize<ModuleMemberRemoveInput>(request.PayloadJson, ModulePayloadJson.Options);
        if (input is null || input.EmployeeId == Guid.Empty)
            return RevertOutcome.NotRevertable("The original request has no employee to restore.");

        var result = await _membership.ApplyMemberAddAsync(request.TenantId, module, context.DeciderEmployeeId, input.EmployeeId, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This member could not be re-added - they may already be a member again, or no longer an active employee.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
