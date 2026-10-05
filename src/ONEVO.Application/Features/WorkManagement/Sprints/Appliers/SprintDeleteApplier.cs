using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>Applies an approved sprint.delete (still Complete/Achieved only); its tasks go back to the
/// backlog. Captures the moved task ids as UndoJson (sprint.delete has no symmetric write-service undo
/// to reuse, unlike achieve/unachieve, so the reverter needs this list explicitly).</summary>
public sealed class SprintDeleteApplier : IApprovalActionApplier
{
    private readonly ISprintRepository _sprints;
    private readonly ISprintWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public SprintDeleteApplier(ISprintRepository sprints, ISprintWriteService writes, IWorkTaskRepository tasks)
    {
        _sprints = sprints;
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.SprintDelete;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return ApplyOutcome.Stale;
        if (request.TargetUpdatedAtSnapshot is { } snap && (sprint.UpdatedAt ?? sprint.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var validation = _writes.ValidateDelete(sprint);
        if (!validation.IsSuccess)
            return ApplyOutcome.Invalid(validation.Error ?? "The sprint change could not be applied.");

        var taskIds = (await _tasks.GetBySprintIdAsync(request.TenantId, sprint.Id, ct)).Select(t => t.Id).ToList();
        var undoJson = JsonSerializer.Serialize(new SprintDeleteUndoSnapshot(taskIds), SprintPayloadJson.Options);

        await _writes.ApplyDeleteAsync(request.TenantId, sprint, ct);
        return ApplyOutcome.Applied(undoJson);
    }
}
