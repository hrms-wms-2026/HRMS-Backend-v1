using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.transfer by transferring the head back - reusing ApplyTransferAsync
/// itself (its membership upsert/deactivate pair runs the same way in either direction).</summary>
public sealed class ModuleTransferReverter : IApprovalActionReverter
{
    private readonly IModuleWriteService _writes;
    private readonly IObjectiveRepository _objectives;

    public ModuleTransferReverter(IModuleWriteService writes, IObjectiveRepository objectives)
    {
        _writes = writes;
        _objectives = objectives;
    }

    public string ActionType => WorkActionTypes.ModuleTransfer;

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

        var undo = JsonSerializer.Deserialize<ModuleTransferInput>(request.UndoStateJson, ModulePayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-transfer snapshot is corrupt.");
        if (module.OwnerId == undo.NewHeadEmployeeId)
            return RevertOutcome.Stale; // already transferred again since

        var result = await _writes.ApplyTransferAsync(request.TenantId, module, undo, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "The previous head is no longer an active employee, so this can't be auto-reverted.");

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
