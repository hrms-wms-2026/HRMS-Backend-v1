using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Reverters;

/// <summary>Undoes an approved module.allocation_extend by subtracting back the hours that were
/// actually applied (AppliedPayloadJson if the approver lowered them, else PayloadJson - Review
/// Focus #3). No UndoJson needed: the applied amount already lives on the request.</summary>
public sealed class ModuleAllocationExtendReverter : IApprovalActionReverter
{
    private readonly IObjectiveRepository _objectives;

    public ModuleAllocationExtendReverter(IObjectiveRepository objectives) => _objectives = objectives;

    public string ActionType => WorkActionTypes.ModuleAllocationExtend;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive)
            return RevertOutcome.Stale;

        var appliedJson = request.AppliedPayloadJson ?? request.PayloadJson;
        var applied = JsonSerializer.Deserialize<ModuleAllocationExtendInput>(appliedJson, ModulePayloadJson.Options);
        if (applied is null)
            return RevertOutcome.NotRevertable("The applied payload is corrupt.");
        if (module.AllocatedHours < applied.RequestedAdditionalHours)
            return RevertOutcome.Conflict("The milestone's allocated hours have since dropped below what this request added - reconcile manually.");

        module.AllocatedHours -= applied.RequestedAdditionalHours;
        module.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(module);

        return RevertOutcome.Reverted(() => module.UpdatedAt);
    }
}
