using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;

public class UnassignTaskCommandHandler : IRequestHandler<UnassignTaskCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public UnassignTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, ITaskAssignmentRepository assignments, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _assignments = assignments;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(UnassignTaskCommand request, CancellationToken ct)
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
            return Result.Forbidden("Only this milestone's owner can unassign tasks.");

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
