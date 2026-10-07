using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.edit by replaying ApplyEditAsync with the pre-edit values
/// SprintEditApplier snapshotted.</summary>
public sealed class SprintEditReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintEditReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintEdit;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<SprintEditInput>(request.UndoStateJson, SprintPayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-edit snapshot is corrupt.");

        var result = await _writes.ApplyEditAsync(request.TenantId, context.DeciderEmployeeId, sprint, undo, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This sprint has changed in a way that blocks reverting this edit.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
