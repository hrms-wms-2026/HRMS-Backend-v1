using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

/// <summary>Applies an approved project.status_template_change: the old ApproveTaskStatusChangeRequest body.</summary>
public sealed class TaskStatusTemplateChangeApplier : IApprovalActionApplier
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusChangeRequestConflictSweeper _sweeper;

    public TaskStatusTemplateChangeApplier(
        ICurrentUser currentUser, ITaskStatusRepository statuses, IWorkTaskRepository tasks,
        ITaskStatusChangeRequestConflictSweeper sweeper)
    {
        _currentUser = currentUser;
        _statuses = statuses;
        _tasks = tasks;
        _sweeper = sweeper;
    }

    public string ActionType => WorkActionTypes.ProjectStatusTemplateChange;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var payload = JsonSerializer.Deserialize<TaskStatusTemplateChangePayload>(context.PayloadJson, TaskPayload.Options);
        if (payload?.Changes is null)
            return ApplyOutcome.Invalid("The status change request has no changes.");

        // Tasks still in a status to delete block approval but don't make the request stale:
        // it stays pending so it can be approved once the tasks are moved, or rejected.
        foreach (var delete in payload.Changes.Deletes)
        {
            if (await _tasks.AnyActiveByStatusIdAsync(request.TenantId, delete.StatusId, ct))
                return ApplyOutcome.Invalid($"Move all tasks out of \"{delete.Name}\" before approving its deletion.");
        }

        var current = await _statuses.GetProjectTemplateAsync(request.TenantId, request.ProjectId, ct);
        var undoJson = JsonSerializer.Serialize(
            new ProjectStatusTemplateUndoSnapshot(current.Select(s => new ProjectStatusTemplateSnapshotEntry(
                s.Id, s.Name, s.DisplayOrder, s.Category, s.Color, s.Visibility, s.MarksTaskComplete)).ToList()),
            TaskPayload.Options);
        var applied = TaskStatusChangeSetApplier.Apply(
            current, payload.Changes, request.TenantId, request.ProjectId, _currentUser.UserId, DateTimeOffset.UtcNow);

        if (applied.Outcome == TaskStatusChangeApplyOutcome.Invalid)
            return ApplyOutcome.Invalid(applied.Message!);
        if (applied.Outcome == TaskStatusChangeApplyOutcome.Stale)
            return ApplyOutcome.Stale;

        foreach (var status in applied.Added)
            await _statuses.AddAsync(status, ct);
        foreach (var status in applied.Modified)
            _statuses.Update(status);
        foreach (var status in applied.Deleted)
            _statuses.Remove(status);

        await _sweeper.MarkConflictingStaleAsync(
            request.TenantId, request.ProjectId, context.DeciderEmployeeId, payload.Changes.Footprint(), request.Id, ct);

        return ApplyOutcome.Applied(undoJson);
    }
}
