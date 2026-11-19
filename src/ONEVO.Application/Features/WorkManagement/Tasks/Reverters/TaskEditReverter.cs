using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>Undoes an approved task.edit by replaying ApplyEditAsync with the pre-edit field values
/// TaskEditApplier snapshotted. A SprintId in the snapshot that differs from the task's current one
/// moves it back, logged the same way the original edit logged the forward move.</summary>
public sealed class TaskEditReverter : IApprovalActionReverter
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public TaskEditReverter(ITaskWriteService writes, IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _objectives = objectives;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskEdit;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null)
            return RevertOutcome.Stale;

        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, task.ObjectiveId, ct);
        if (objective is null)
            return RevertOutcome.Stale;

        var undo = JsonSerializer.Deserialize<TaskEditInput>(request.UndoStateJson, TaskPayload.Options);
        if (undo is null)
            return RevertOutcome.NotRevertable("The pre-edit snapshot is corrupt.");

        var result = await _writes.ApplyEditAsync(request.TenantId, context.DeciderEmployeeId, task, objective, undo,
            TaskEditLogSources.Reverted, request.Id, ct);
        if (!result.IsSuccess)
            return RevertOutcome.Conflict(result.Error ?? "This task has changed in a way that blocks reverting this edit.");

        return RevertOutcome.Reverted(() => task.UpdatedAt);
    }
}
