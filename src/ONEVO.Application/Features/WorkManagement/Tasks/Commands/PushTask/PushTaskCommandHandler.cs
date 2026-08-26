using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;

public class PushTaskCommandHandler : IRequestHandler<PushTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ITaskPercentageLogRepository _percentageLogs;
    private readonly IUnitOfWork _unitOfWork;

    public PushTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskClockingSessionRepository sessions, ITaskPercentageLogRepository percentageLogs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _sessions = sessions;
        _percentageLogs = percentageLogs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(PushTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var openSession = await _sessions.GetOpenSessionForTaskAsync(tenantId, task.Id, ct);
        if (openSession is null)
            return Result<WorkTaskResponse>.Conflict("This task has no open clock-in session to push.");

        if (openSession.EmployeeId != callerEmployeeId.Value)
            return Result<WorkTaskResponse>.Forbidden("Only the employee who clocked in can push this session.");

        if (request.Percent <= task.ProgressPercent)
            return Result<WorkTaskResponse>.Failure(
                $"Percent must be greater than the task's current progress ({task.ProgressPercent}%).", 400);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var trackedSession = await _sessions.GetTrackedByIdForTenantAsync(tenantId, openSession.Id, innerCt);
            if (trackedSession is null)
                return Result<WorkTaskResponse>.NotFound("Clocking session not found.");

            trackedSession.ClockOutAt = now;
            trackedSession.DurationMinutes = (int)(now - trackedSession.ClockInAt).TotalMinutes;
            trackedSession.UpdatedAt = now;
            _sessions.Update(trackedSession);

            var previousPercent = task.ProgressPercent;
            task.ProgressPercent = request.Percent;
            task.UpdatedAt = now;

            await _percentageLogs.AddAsync(new TaskPercentageLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, PreviousPercent = previousPercent,
                NewPercent = task.ProgressPercent, Source = TaskPercentageLogSources.Push,
                ClockingSessionId = trackedSession.Id, Reason = request.Reason?.Trim(), ChangedAt = now
            }, innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId));
        }, ct);
    }
}
