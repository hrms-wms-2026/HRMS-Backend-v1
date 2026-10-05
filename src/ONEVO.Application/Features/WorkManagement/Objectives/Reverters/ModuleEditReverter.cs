using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.edit by replaying ApplyEditAsync with the pre-edit field values
/// ModuleEditApplier snapshotted into UndoStateJson.</summary>
public sealed class ModuleEditReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleEditReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleEdit;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<ModuleEditInput>(request.UndoStateJson, ModulePayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-edit snapshot is corrupt.");

        var result = await _writes.ApplyEditAsync(request.TenantId, module, undo, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "The milestone has changed in a way that blocks reverting this edit.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
