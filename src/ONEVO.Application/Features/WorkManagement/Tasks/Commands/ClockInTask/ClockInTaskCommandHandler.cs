using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public class ClockInTaskCommandHandler : IRequestHandler<ClockInTaskCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;

    public ClockInTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments, ITaskClockingSessionRepository sessions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ClockInTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        if (await _assignments.GetByTaskAndEmployeeAsync(task.Id, callerEmployeeId.Value, ct) is null)
            return Result.Forbidden("Only an assignee of this task can clock in.");

        if (task.ProgressPercent == 100)
            return Result.Conflict("This task is complete - reduce its percentage before clocking in again.");

        if (await _sessions.GetOpenSessionForTaskAsync(tenantId, task.Id, ct) is not null)
            return Result.Conflict("This task already has an open clock-in session.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _sessions.AddAsync(new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, ClockInAt = DateTimeOffset.UtcNow
            }, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
