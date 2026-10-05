using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.unachieve by calling ApplyAchieveAsync - reusing its validation
/// (every direct child already achieved, no tasks in an Active sprint) as the Conflict guard.</summary>
public sealed class ModuleUnachieveReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleUnachieveReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleUnachieve;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || module.IsAchieved)
            return RevertOutcome.Stale;

        var result = await _writes.ApplyAchieveAsync(request.TenantId, module, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This milestone can no longer be re-achieved automatically.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
