using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;

public class MoveTaskStatusCommandHandler : IRequestHandler<MoveTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public MoveTaskStatusCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MoveTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var newStatus = await _statuses.GetByIdForTenantAsync(tenantId, request.NewStatusId, ct);
        if (newStatus is null)
            return Result.NotFound("Target status not found.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            task.StatusId = newStatus.Id;
            if (newStatus.MarksTaskComplete)
            {
                task.CompletedAt = DateTimeOffset.UtcNow;
                task.ProgressPercent = 100;
            }
            task.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
