using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.achieve via ISprintWriteService.ApplyUnachieveAsync (Task 3),
/// restoring the exact status the sprint had right before it was achieved.</summary>
public sealed class SprintAchieveReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintAchieveReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintAchieve;

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

        var undo = JsonSerializer.Deserialize<SprintAchieveUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-achieve snapshot is corrupt.");

        var result = await _writes.ApplyUnachieveAsync(request.TenantId, context.DeciderEmployeeId, sprint, undo.PreviousStatus, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This sprint is no longer achieved.");

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
