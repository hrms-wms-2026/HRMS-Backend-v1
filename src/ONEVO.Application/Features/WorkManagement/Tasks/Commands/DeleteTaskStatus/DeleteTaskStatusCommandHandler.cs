using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;

public class DeleteTaskStatusCommandHandler : IRequestHandler<DeleteTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public DeleteTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ITaskStatusRepository statuses, IWorkTaskRepository tasks, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _statuses = statuses;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(DeleteTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var status = await _statuses.GetByIdForTenantAsync(tenantId, request.StatusId, ct);
        if (status is null || status.ObjectiveId is null)
            return Result.NotFound("Task status not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, status.ObjectiveId.Value, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only this milestone's owner can delete task statuses.");

        if (await _tasks.AnyActiveByStatusIdAsync(tenantId, status.Id, ct))
            return Result.Conflict("Move all tasks out of this status before deleting it.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _statuses.Remove(status);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
