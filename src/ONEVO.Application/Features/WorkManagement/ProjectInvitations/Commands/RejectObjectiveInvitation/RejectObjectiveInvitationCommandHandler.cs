using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;

public class RejectObjectiveInvitationCommandHandler : IRequestHandler<RejectObjectiveInvitationCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public RejectObjectiveInvitationCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectMemberInvitationRepository invitations,
        IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _invitations = invitations;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectObjectiveInvitationCommand request, CancellationToken ct)
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

        var invitation = await _invitations.GetTrackedByIdForTenantAsync(tenantId, request.InvitationId, ct);
        if (invitation is null)
            return Result.NotFound("Invitation not found.");

        if (invitation.InvitedEmployeeId != callerEmployeeId.Value)
            return Result.Forbidden("Only the invited employee can reject this invitation.");

        if (invitation.Status != ProjectInvitationStatuses.Pending)
            return Result.Conflict("This invitation has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, invitation.ObjectiveId, ct);
        if (objective is not null && objective.IsAchieved)
            return Result.Failure("Cannot reject an invitation on an achieved milestone.");

        invitation.Status = ProjectInvitationStatuses.Declined;
        invitation.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(invitation);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
