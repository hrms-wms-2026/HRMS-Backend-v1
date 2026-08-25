using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;

public class MoveTaskStatusCommandHandler : IRequestHandler<MoveTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;

    public MoveTaskStatusCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IWorkTaskRepository tasks,
        ITaskStatusRepository statuses,
        IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership,
        IUnitOfWork unitOfWork,
        ISprintRepository sprints)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _statuses = statuses;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
        _sprints = sprints;
    }

    public async Task<Result> Handle(MoveTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(
            tenantId,
            _currentUser.UserId,
            ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var newStatus = await _statuses.GetByIdForTenantAsync(tenantId, request.NewStatusId, ct);
        if (newStatus is null || newStatus.ProjectId != task.ProjectId)
            return Result.NotFound("Target status not found.");

        var objective = await _objectives.GetTrackedByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
        {
            var isMember = await _membership.IsActiveMemberAsync(
                tenantId,
                objective.Id,
                callerEmployeeId.Value,
                ct);
            if (!isMember)
                return Result.Forbidden("Only active milestone members can move tasks.");
            if (newStatus.Visibility == TaskStatusVisibilities.Private)
                return Result.Forbidden("Only the milestone owner can move a task into this status.");
        }

        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result.Forbidden("This task's sprint has been achieved and is now frozen.");
        }

        var oldStatus = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, ct);
        var wasComplete = oldStatus?.MarksTaskComplete ?? false;
        var willBeComplete = newStatus.MarksTaskComplete;

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            task.StatusId = newStatus.Id;

            if (!wasComplete && willBeComplete)
            {
                task.CompletedHours = task.EstimatedHours ?? 0m;
                task.CompletedAt = DateTimeOffset.UtcNow;
                task.ProgressPercent = 100;
                objective.CompletedHours += task.CompletedHours;
            }
            else if (wasComplete && !willBeComplete)
            {
                objective.CompletedHours -= task.CompletedHours;
                task.CompletedHours = 0m;
                task.CompletedAt = null;
                task.ProgressPercent = 0;
            }

            task.UpdatedAt = DateTimeOffset.UtcNow;
            objective.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
