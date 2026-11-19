using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.complete via ISprintWriteService.ApplyUncompleteAsync (Task 3),
/// using the moved-task-id list SprintCompleteApplier snapshotted and the disposition still on the
/// request's applied payload (Review Focus #3).</summary>
public sealed class SprintCompleteReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintCompleteReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintComplete;

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

        var undo = JsonSerializer.Deserialize<SprintCompleteUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);
        var appliedInput = JsonSerializer.Deserialize<SprintCompleteInput>(request.AppliedPayloadJson ?? request.PayloadJson, SprintPayloadJson.Options);
        if (undo is null || appliedInput is null)
            return RevertOutcome.NotRevertable("The completion snapshot is corrupt.");

        var targetSprintId = appliedInput.Disposition == "sprint" ? appliedInput.TargetSprintId : null;
        var result = await _writes.ApplyUncompleteAsync(request.TenantId, context.DeciderEmployeeId, sprint, undo.MovedTaskIds, targetSprintId, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This sprint's tasks have moved since it was completed.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
