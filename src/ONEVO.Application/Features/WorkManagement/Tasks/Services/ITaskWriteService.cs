using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// The single home of task create/edit/delete validation and mutation, shared by the direct
/// command handlers and the approval appliers so an approved request goes through exactly the same
/// rules as a direct change. Permission checks stay in the callers. Never calls SaveChangesAsync.
/// </summary>
public interface ITaskWriteService
{
    /// <summary>Every read-only check a create needs (objective active, project, event window, default status, category, sprint, slack).</summary>
    Task<Result> ValidateCreateAsync(Guid tenantId, TaskCreateInput input, CancellationToken ct = default);

    /// <summary>Re-validates, then inserts the task (+ sprint log). No SaveChanges.</summary>
    Task<Result<WorkTask>> CreateAsync(Guid tenantId, Guid creatorUserId, Guid actorEmployeeId, TaskCreateInput input,
        Guid creatorPositionObjectiveId, CancellationToken ct = default);

    /// <summary>Every read-only check an edit needs (frozen sprint, target sprint, event window, slack).</summary>
    Task<Result> ValidateEditAsync(Guid tenantId, WorkTask task, Objective objective, TaskEditInput input, CancellationToken ct = default);

    /// <summary>Re-validates, then mutates the tracked task and writes edit/percentage/sprint logs attributed to
    /// actorEmployeeId. No SaveChanges.</summary>
    Task<Result> ApplyEditAsync(Guid tenantId, Guid actorEmployeeId, WorkTask trackedTask, Objective objective,
        TaskEditInput input, string editLogSource, Guid? approvalRequestId, CancellationToken ct = default);

    /// <summary>Removes the task. No SaveChanges.</summary>
    void Delete(WorkTask trackedTask);

    /// <summary>Undoes Delete: clears the soft-delete flags set by SoftDeleteInterceptor.</summary>
    void Restore(WorkTask trackedTask);
}
