using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;

public class AssignTaskCommandHandler : IRequestHandler<AssignTaskCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeeChecklistTaskRepository? _checklistTasks;
    private readonly IWorkNotificationEngine? _notifications;

    public AssignTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, ITaskAssignmentRepository assignments,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork,
        IEmployeeChecklistTaskRepository? checklistTasks = null,
        IWorkNotificationEngine? notifications = null)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _assignments = assignments;
        _membership = membership;
        _unitOfWork = unitOfWork;
        _checklistTasks = checklistTasks;
        _notifications = notifications;
    }

    public async Task<Result> Handle(AssignTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only this milestone's owner can assign tasks.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result.Failure("The assignee must be an active employee in this tenant.");

        ONEVO.Domain.Features.CoreHr.Entities.EmployeeChecklistTask? checklistTask = null;
        if (task.TaskKind == WorkTaskKinds.EmployeeChecklist)
        {
            checklistTask = _checklistTasks is null
                ? null
                : await _checklistTasks.GetTrackedByWorkTaskIdAsync(tenantId, task.Id, ct);
            if (checklistTask is null)
                return Result.Conflict("The linked employee checklist item could not be found.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var existingAssignments = await _assignments.GetByTaskIdAsync(task.Id, innerCt);
            if (existingAssignments.Count == 1 && existingAssignments[0].EmployeeId == assignee.Id
                && (checklistTask is null || checklistTask.AssignedToId == assignee.UserId))
                return Result.Success();

            foreach (var existingAssignment in existingAssignments)
                _assignments.Remove(existingAssignment);

            await _assignments.AddAsync(new TaskAssignment
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                UserId = assignee.UserId,
                EmployeeId = assignee.Id,
                AssignedById = callerEmployeeId.Value,
                AssignedAt = DateTimeOffset.UtcNow
            }, innerCt);
            await _membership.UpsertMembershipAsync(
                tenantId, task.ProjectId, task.ObjectiveId, assignee.Id, innerCt);
            if (checklistTask is not null)
                checklistTask.AssignedToId = assignee.UserId;
            if (_notifications is not null)
            {
                await _notifications.NotifyAsync(new WorkNotificationEvent(
                    tenantId, task.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
                    WorkActionTypes.TaskEdit, WorkTargetTypes.Task, task.Id, task.Title, null,
                    [assignee.Id]), innerCt);
            }
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
