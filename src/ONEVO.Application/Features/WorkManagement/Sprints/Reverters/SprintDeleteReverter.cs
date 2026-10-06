using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.delete via ISprintWriteService.Restore (Task 3): clears the
/// soft-delete flags and reattaches the tasks SprintDeleteApplier detached, Conflict if any of them has
/// since joined a different sprint.</summary>
public sealed class SprintDeleteReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintDeleteReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintDelete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantIncludingDeletedAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null || !sprint.IsDeleted)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<SprintDeleteUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The deleted-tasks snapshot is corrupt.");

        var result = await _writes.Restore(request.TenantId, sprint, undo.TaskIds, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "A task from this sprint has since moved elsewhere.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
