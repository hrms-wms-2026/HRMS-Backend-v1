using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;

public class UnassignTaskCommandHandler : IRequestHandler<UnassignTaskCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IUnitOfWork _unitOfWork;

    public UnassignTaskCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, ITaskAssignmentRepository assignments, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _assignments = assignments;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnassignTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var task = await _tasks.GetByIdForTenantAsync(_currentUser.TenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var assignment = await _assignments.GetByTaskAndEmployeeAsync(request.TaskId, request.EmployeeId, ct);
        if (assignment is null)
            return Result.NotFound("Assignment not found.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _assignments.Remove(assignment);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
