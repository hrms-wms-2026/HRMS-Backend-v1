using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public class AddObjectiveMemberCommandHandler : IRequestHandler<AddObjectiveMemberCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public AddObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot add members to an achieved milestone.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's head can add members.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result.Failure("The member must be an active employee in this tenant.");

        await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.EmployeeId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
