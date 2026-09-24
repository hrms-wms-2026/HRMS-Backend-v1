using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public class ClockInTaskCommandHandler : IRequestHandler<ClockInTaskCommand, Result<ClockInTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskStatusChangeLogRepository _statusChangeLogs;
    private readonly IUnitOfWork _unitOfWork;

    public ClockInTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments, ITaskClockingSessionRepository sessions,
        ITaskStatusRepository statuses, ITaskStatusChangeLogRepository statusChangeLogs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
        _statuses = statuses;
        _statusChangeLogs = statusChangeLogs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClockInTaskResponse>> Handle(ClockInTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ClockInTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ClockInTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<ClockInTaskResponse>.NotFound("Task not found.");

        if (await _assignments.GetByTaskAndEmployeeAsync(task.Id, callerEmployeeId.Value, ct) is null)
            return Result<ClockInTaskResponse>.Forbidden("Only an assignee of this task can clock in.");

        if (task.ProgressPercent == 100)
            return Result<ClockInTaskResponse>.Conflict("This task is complete - reduce its percentage before clocking in again.");

        if (await _sessions.GetOpenSessionForTaskAsync(tenantId, task.Id, ct) is not null)
            return Result<ClockInTaskResponse>.Conflict("This task already has an open clock-in session.");

        var currentStatus = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, ct);
        ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus? moveTarget = null;
        if (currentStatus?.Category == TaskStatusCategories.NotStarted)
        {
            moveTarget = (await _statuses.GetProjectTemplateAsync(tenantId, task.ProjectId, ct))
                .Where(s => s.Category == TaskStatusCategories.Active && s.Visibility == TaskStatusVisibilities.Public)
                .OrderBy(s => s.DisplayOrder)
                .FirstOrDefault();
            if (moveTarget is null)
                return Result<ClockInTaskResponse>.Conflict("Every Active status on this project is private - ask the module owner to move this task first.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            await _sessions.AddAsync(new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, ClockInAt = now
            }, innerCt);
            TaskStatusMoveInfo? movedToStatus = null;
            if (moveTarget is not null)
            {
                var fromStatusId = task.StatusId;
                task.StatusId = moveTarget.Id;
                task.UpdatedAt = now;
                _tasks.Update(task);
                await _statusChangeLogs.AddAsync(new TaskStatusChangeLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, FromStatusId = fromStatusId,
                    ToStatusId = moveTarget.Id, ChangedAt = now
                }, innerCt);
                movedToStatus = new TaskStatusMoveInfo(moveTarget.Id, moveTarget.Name, moveTarget.Color);
            }
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<ClockInTaskResponse>.Success(new ClockInTaskResponse(movedToStatus));
        }, ct);
    }
}


