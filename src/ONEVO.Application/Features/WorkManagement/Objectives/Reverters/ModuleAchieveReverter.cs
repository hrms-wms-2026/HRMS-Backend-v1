using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.achieve by calling ApplyUnachieveAsync - the exact inverse
/// operation, with its own validation (current head must be active) reused as-is.</summary>
public sealed class ModuleAchieveReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleAchieveReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleAchieve;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsAchieved)
            return RevertOutcome.Stale;

        var result = await _writes.ApplyUnachieveAsync(request.TenantId, module, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "The milestone's current head is no longer active, so this can't be auto-reverted.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
