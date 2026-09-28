using System.Text.Json;
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
using ONEVO.Application.Features.WorkManagement.Tasks.Mappers;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

/// <summary>
/// Edits a task through the approval engine: a caller at or above the task's creator position edits
/// it now; any other Module member files a task.edit approval request.
/// </summary>
public class EditTaskCommandHandler : IRequestHandler<EditTaskCommand, Result<TaskWriteOutcome>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ITaskWriteService _writes;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IWorkNotificationEngine _notifications;

    public EditTaskCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, IObjectiveRepository objectives,
        IUnitOfWork unitOfWork, ICallerIdentityResolver identity, IMilestoneMembershipCoordinator membership,
        ITaskAssignmentRepository assignments, ITaskAssetLinker assetLinker, ITaskWriteService writes,
        IWorkHierarchyService hierarchy, IWorkApprovalEngine approvals, IWorkNotificationEngine notifications)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _identity = identity;
        _membership = membership;
        _assignments = assignments;
        _assetLinker = assetLinker;
        _writes = writes;
        _hierarchy = hierarchy;
        _approvals = approvals;
        _notifications = notifications;
    }

    public async Task<Result<TaskWriteOutcome>> Handle(EditTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskWriteOutcome>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<TaskWriteOutcome>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<TaskWriteOutcome>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<TaskWriteOutcome>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<TaskWriteOutcome>.Forbidden("Only members of this module can change its tasks.");

        var input = new TaskEditInput(request.Title.Trim(), request.Description?.Trim(), request.Priority, request.DueDate,
            request.EstimatedHours, request.StoryPoints, request.ProgressPercent, request.Reason?.Trim(), request.SprintId);

        var validation = await _writes.ValidateEditAsync(tenantId, task, objective, input, ct);
        if (!validation.IsSuccess)
            return Result<TaskWriteOutcome>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var tree = await _hierarchy.LoadTreeAsync(tenantId, objective.ProjectId, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var decision = await _approvals.SubmitAsync(new WorkAction(
                tenantId, objective.ProjectId, callerEmployeeId.Value,
                WorkActionTypes.TaskEdit, WorkTargetTypes.Task,
                TargetId: task.Id, TargetTitle: task.Title,
                TargetModuleId: objective.Id, PositionModuleId: task.CreatorPositionObjectiveId,
                PayloadJson: JsonSerializer.Serialize(input), TargetUpdatedAt: task.UpdatedAt ?? task.CreatedAt), innerCt);
            if (!decision.IsSuccess)
                return Result<TaskWriteOutcome>.Failure(decision.Error!, decision.StatusCode ?? 400);

            if (!decision.Value!.IsDirect)
            {
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, decision.Value.ApprovalRequestId));
            }

            var applied = await _writes.ApplyEditAsync(tenantId, callerEmployeeId.Value, task, objective, input,
                TaskEditLogSources.Direct, null, innerCt);
            if (!applied.IsSuccess)
                return Result<TaskWriteOutcome>.Failure(applied.Error!, applied.StatusCode ?? 400);

            var assigneeIds = (await _assignments.GetByTaskIdAsync(task.Id, innerCt)).Select(a => a.EmployeeId).ToList();

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, objective.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
                WorkActionTypes.TaskEdit, WorkTargetTypes.Task, task.Id, task.Title, null,
                TaskChangeRecipients.For(tree, task, objective, assigneeIds)), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            await _assetLinker.SyncAttachmentsAsync(tenantId, userId, task.Id, request.AttachmentFileIds ?? Array.Empty<Guid>(), innerCt);
            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, task.Id, task.Description, innerCt);

            return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(WorkTaskResponseMapper.ToResponse(task, assigneeIds), null));
        }, ct);
    }
}
