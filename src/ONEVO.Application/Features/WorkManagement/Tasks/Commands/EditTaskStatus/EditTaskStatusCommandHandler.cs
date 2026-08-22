using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public class EditTaskStatusCommandHandler : IRequestHandler<EditTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public EditTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskStatusRepository statuses,
        IObjectiveRepository objectives, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _statuses = statuses;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(EditTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var status = await _statuses.GetByIdForTenantAsync(tenantId, request.StatusId, ct);
        if (status is null || status.ObjectiveId is null)
            return Result.NotFound("Task status not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, status.ObjectiveId.Value, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only this milestone's owner can change task status configuration.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            status.Name = request.Name.Trim();
            status.DisplayOrder = request.DisplayOrder;
            status.RequiresApproval = request.RequiresApproval;
            status.ApproverId = request.ApproverId;
            status.Visibility = request.Visibility;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            _statuses.Update(status);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
