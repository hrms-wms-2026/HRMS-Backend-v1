using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

/// <summary>Applies an approved task.edit, attributed to the requester. Stale if the task changed after the request was made.</summary>
public sealed class TaskEditApplier : IApprovalActionApplier
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public TaskEditApplier(ITaskWriteService writes, IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _objectives = objectives;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskEdit;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null)
            return ApplyOutcome.Stale;
        if (request.TargetUpdatedAtSnapshot is { } snap && (task.UpdatedAt ?? task.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, task.ObjectiveId, ct);
        if (objective is null)
            return ApplyOutcome.Stale;

        var input = JsonSerializer.Deserialize<TaskEditInput>(context.PayloadJson, TaskPayload.Options);
        if (input is null || string.IsNullOrWhiteSpace(input.Title))
            return ApplyOutcome.Invalid("The task edit request has no title.");

        var applied = await _writes.ApplyEditAsync(request.TenantId, request.RequestedByEmployeeId, task, objective, input,
            TaskEditLogSources.ApprovedRequest, request.Id, ct);
        return applied.IsSuccess
            ? ApplyOutcome.Applied
            : ApplyOutcome.Invalid(applied.Error ?? "The task edit is not valid.");
    }
}
