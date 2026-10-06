using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>
/// Undoes an approved project.status_template_change by reconciling the live template back to the
/// whole-template snapshot TaskStatusTemplateChangeApplier captured before it ran - not a diff replay
/// like TaskStatusChangeSetApplier (that type describes *changes*, this restores an exact prior state).
/// Refuses (Conflict) if a status the original apply added now has active tasks on it, since deleting
/// it to restore the snapshot would orphan them - the same rule ApplyAsync itself uses for an ordinary
/// delete.
/// </summary>
public sealed class ProjectStatusTemplateChangeReverter : IApprovalActionReverter
{
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;

    public ProjectStatusTemplateChangeReverter(ITaskStatusRepository statuses, IWorkTaskRepository tasks)
    {
        _statuses = statuses;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.ProjectStatusTemplateChange;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.UndoStateJson is null)
            return RevertOutcome.NotRevertable("Nothing to undo for this request.");

        var snapshot = JsonSerializer.Deserialize<ProjectStatusTemplateUndoSnapshot>(request.UndoStateJson);
        if (snapshot is null)
            return RevertOutcome.NotRevertable("The pre-change template snapshot is corrupt.");

        var current = await _statuses.GetProjectTemplateAsync(request.TenantId, request.ProjectId, ct);
        var currentById = current.ToDictionary(s => s.Id);
        var snapshotById = snapshot.Statuses.ToDictionary(s => s.Id);

        // Statuses in the live template but not the snapshot were added by the original apply - undo
        // deletes them, but only if nothing has since started using one.
        var toDelete = current.Where(s => !snapshotById.ContainsKey(s.Id)).ToList();
        foreach (var status in toDelete)
        {
            if (await _tasks.AnyActiveByStatusIdAsync(request.TenantId, status.Id, ct))
                return RevertOutcome.Conflict($"\"{status.Name}\" was added by this change and now has tasks on it - move them out before reverting.");
        }

        foreach (var status in toDelete)
            _statuses.Remove(status);

        // Statuses in the snapshot but not the live template were deleted by the original apply -
        // undo re-creates them with their original id (nothing still references it, by the same
        // guarantee the original delete's own active-task check gave).
        foreach (var entry in snapshot.Statuses.Where(s => !currentById.ContainsKey(s.Id)))
        {
            await _statuses.AddAsync(new TaskStatusEntity
            {
                Id = entry.Id, TenantId = request.TenantId, ProjectId = request.ProjectId, ObjectiveId = null,
                Name = entry.Name, DisplayOrder = entry.DisplayOrder, Category = entry.Category,
                Color = entry.Color, Visibility = entry.Visibility, MarksTaskComplete = entry.MarksTaskComplete
            }, ct);
        }

        // Statuses present in both were (possibly) modified by the original apply - restore their
        // fields to the snapshot's values.
        foreach (var status in current)
        {
            if (!snapshotById.TryGetValue(status.Id, out var entry))
                continue;
            status.Name = entry.Name;
            status.DisplayOrder = entry.DisplayOrder;
            status.Category = entry.Category;
            status.Color = entry.Color;
            status.Visibility = entry.Visibility;
            status.MarksTaskComplete = entry.MarksTaskComplete;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            _statuses.Update(status);
        }

        // No TargetUpdatedAtSnapshot to re-baseline: this action type has no single TargetId (it's
        // project-wide), so ModuleApplierBase/SprintApplierBase's staleness check never applied to it.
        return RevertOutcome.Reverted();
    }
}
