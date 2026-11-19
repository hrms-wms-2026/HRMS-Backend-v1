using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.start: back to Draft, dates cleared, Goal restored to whatever it
/// was (if anything) right before Start set it.</summary>
public sealed class SprintStartReverter : IApprovalActionReverter
{
    private readonly ISprintRepository _sprints;

    public SprintStartReverter(ISprintRepository sprints) => _sprints = sprints;

    public string ActionType => WorkActionTypes.SprintStart;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null || sprint.Status != SprintStatuses.Active)
            return RevertOutcome.Stale;

        var undo = request.UndoStateJson is null
            ? null
            : JsonSerializer.Deserialize<SprintStartUndoSnapshot>(request.UndoStateJson, SprintPayloadJson.Options);

        sprint.Status = SprintStatuses.Draft;
        sprint.StartDate = null;
        sprint.EndDate = null;
        if (undo is not null)
            sprint.Goal = undo.PreviousGoal;
        sprint.UpdatedAt = DateTimeOffset.UtcNow;

        return RevertOutcome.Reverted(() => sprint.UpdatedAt);
    }
}
