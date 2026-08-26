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
    private readonly ITaskStatusChangeLogRepository _statusChangeLogs;
    private readonly ITaskPercentageLogRepository _percentageLogs;
    private readonly ITaskClockingSessionRepository _clockingSessions;

    public MoveTaskStatusCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IWorkTaskRepository tasks,
        ITaskStatusRepository statuses,
        IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership,
                IUnitOfWork unitOfWork,
        ISprintRepository sprints,
        ITaskStatusChangeLogRepository statusChangeLogs,
        ITaskPercentageLogRepository percentageLogs,
        ITaskClockingSessionRepository clockingSessions)

    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _statuses = statuses;
        _objectives = objectives;
        _membership = membership;
                _unitOfWork = unitOfWork;
        _sprints = sprints;
        _statusChangeLogs = statusChangeLogs;
        _percentageLogs = percentageLogs;
        _clockingSessions = clockingSessions;

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
            var now = DateTimeOffset.UtcNow;
            var fromStatusId = task.StatusId;
            task.StatusId = newStatus.Id;

                        if (!wasComplete && willBeComplete)
            {
                task.CompletedHours = task.EstimatedHours ?? 0m;
                task.CompletedAt = now;
                var previousPercent = task.ProgressPercent;
                task.ProgressPercent = 100;
                objective.CompletedHours += task.CompletedHours;
                await _percentageLogs.AddAsync(new TaskPercentageLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, PreviousPercent = previousPercent, NewPercent = 100,
                    Source = TaskPercentageLogSources.StatusChange, ClockingSessionId = null, ChangedAt = now
                }, innerCt);

                // A status-driven completion locks clocking the same way a 100% Push does (spec §4),
                // but unlike Push there is no caller-supplied session to close - an assignee may still
                // have this task clocked in when someone else drags it to a complete column. Leaving
                // that session open would be unclosable forever (Push requires percent > 100, which is
                // impossible) and would permanently block re-clocking via the partial unique index, even
                // after a later edit unlocks the task by resetting ProgressPercent below 100.
                var openSession = await _clockingSessions.GetOpenSessionForTaskAsync(tenantId, task.Id, innerCt);
                if (openSession is not null)
                {
                    var trackedSession = await _clockingSessions.GetTrackedByIdForTenantAsync(tenantId, openSession.Id, innerCt);
                    if (trackedSession is not null)
                    {
                        trackedSession.ClockOutAt = now;
                        trackedSession.DurationMinutes = (int)(now - trackedSession.ClockInAt).TotalMinutes;
                        trackedSession.UpdatedAt = now;
                        _clockingSessions.Update(trackedSession);
                    }
                }
            }

            else if (wasComplete && !willBeComplete)
            {
                                objective.CompletedHours -= task.CompletedHours;
                task.CompletedHours = 0m;
                task.CompletedAt = null;
                var previousPercent = task.ProgressPercent;
                task.ProgressPercent = 0;
                await _percentageLogs.AddAsync(new TaskPercentageLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, PreviousPercent = previousPercent, NewPercent = 0,
                    Source = TaskPercentageLogSources.StatusChange, ClockingSessionId = null, ChangedAt = now
                }, innerCt);

            }

                        await _statusChangeLogs.AddAsync(new TaskStatusChangeLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, FromStatusId = fromStatusId, ToStatusId = newStatus.Id,
                ChangedAt = now
            }, innerCt);

            task.UpdatedAt = now;
            objective.UpdatedAt = now;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result.Success();
        }, ct);
    }
}
