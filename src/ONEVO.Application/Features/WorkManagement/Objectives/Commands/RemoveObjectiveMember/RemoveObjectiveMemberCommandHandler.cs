using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public class RemoveObjectiveMemberCommandHandler : IRequestHandler<RemoveObjectiveMemberCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveObjectiveMemberCommand request, CancellationToken ct)
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
            return Result.Failure("Cannot remove members from an achieved milestone.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's head can remove members.");

        if (request.EmployeeId == objective.OwnerId)
            return Result.Failure("Cannot remove the milestone's head as a member - use Transfer instead.");

        if (await _membership.HasActiveMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.EmployeeId, ct))
        {
            await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.EmployeeId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var pendingInvite = await _invitations.GetTrackedPendingForObjectiveAndEmployeeAsync(tenantId, objective.Id, request.EmployeeId, ct);
        if (pendingInvite is null)
            return Result.NotFound("This employee has no active membership or pending invitation on this milestone.");

        pendingInvite.Status = ProjectInvitationStatuses.Cancelled;
        pendingInvite.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(pendingInvite);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
