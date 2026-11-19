using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;

/// <summary>
/// Deletes a task through the approval engine. The outcome's Task is always null; a null
/// ApprovalRequestId means the task was deleted, otherwise the deletion was sent for approval.
/// </summary>
public class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand, Result<TaskWriteOutcome>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskWriteService _writes;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IWorkNotificationEngine _notifications;

    public DeleteTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership,
        ITaskAssignmentRepository assignments, ITaskWriteService writes, IWorkHierarchyService hierarchy,
        IWorkApprovalEngine approvals, IWorkNotificationEngine notifications)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _assignments = assignments;
        _writes = writes;
        _hierarchy = hierarchy;
        _approvals = approvals;
        _notifications = notifications;
    }

    public async Task<Result<TaskWriteOutcome>> Handle(DeleteTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskWriteOutcome>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskWriteOutcome>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<TaskWriteOutcome>.NotFound("Task not found.");
        if (task.TaskKind == WorkTaskKinds.EmployeeChecklist)
            return Result<TaskWriteOutcome>.Conflict("Checklist tasks cannot be deleted. Update the linked employee checklist instead.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<TaskWriteOutcome>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<TaskWriteOutcome>.Forbidden("Only members of this module can change its tasks.");

        var children = await _tasks.GetTrackedByParentTaskIdAsync(tenantId, task.Id, ct);
        if (children is { Count: > 0 })
            return Result<TaskWriteOutcome>.Conflict("This task has subtasks. Delete or move its subtasks first.");

        var tree = await _hierarchy.LoadTreeAsync(tenantId, objective.ProjectId, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var decision = await _approvals.SubmitAsync(new WorkAction(
                tenantId, objective.ProjectId, callerEmployeeId.Value,
                WorkActionTypes.TaskDelete, WorkTargetTypes.Task,
                TargetId: task.Id, TargetTitle: task.Title,
                TargetModuleId: objective.Id, PositionModuleId: task.CreatorPositionObjectiveId,
                PayloadJson: "{}", TargetUpdatedAt: task.UpdatedAt ?? task.CreatedAt), innerCt);
            if (!decision.IsSuccess)
                return Result<TaskWriteOutcome>.Failure(decision.Error!, decision.StatusCode ?? 400);

            if (!decision.Value!.IsDirect)
            {
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, decision.Value.ApprovalRequestId));
            }

            // Read the assignees before the task (and its assignment rows) go away.
            var assigneeIds = (await _assignments.GetByTaskIdAsync(task.Id, innerCt)).Select(a => a.EmployeeId).ToList();
            _writes.Delete(task);

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, objective.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
                WorkActionTypes.TaskDelete, WorkTargetTypes.Task, task.Id, task.Title, null,
                TaskChangeRecipients.For(tree, task, objective, assigneeIds)), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, null));
        }, ct);
    }
}
