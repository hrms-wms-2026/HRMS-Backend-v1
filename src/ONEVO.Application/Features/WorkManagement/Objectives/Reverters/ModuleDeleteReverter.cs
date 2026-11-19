using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.delete by clearing the IsActive flag ApplyDeleteAsync set.</summary>
public sealed class ModuleDeleteReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleDeleteReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleDelete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || module.IsActive)
            return RevertOutcome.Stale;

        _writes.Restore(module);
        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
