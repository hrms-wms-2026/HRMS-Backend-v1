using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.CancelObjectiveInvitation;

/// <summary>Lets the sender of a pending module invitation withdraw it, within the same 30-minute
/// window a decider gets to revert an approval decision (ApprovalRevertWindow.Minutes) - the invitee's
/// own Accept/Reject stays available to them the whole time this hasn't fired.</summary>
public class CancelObjectiveInvitationCommandHandler : IRequestHandler<CancelObjectiveInvitationCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;

    public CancelObjectiveInvitationCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectMemberInvitationRepository invitations,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CancelObjectiveInvitationCommand request, CancellationToken ct)
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

        if (invitation.InvitedById != callerEmployeeId.Value)
            return Result.Forbidden("Only the person who sent this invitation can cancel it.");

        if (invitation.Status != ProjectInvitationStatuses.Pending)
            return Result.Conflict("This invitation has already been decided.");

        if (DateTimeOffset.UtcNow > invitation.CreatedAt.AddMinutes(ApprovalRevertWindow.Minutes))
            return Result.Conflict("The 30-minute window to cancel this invitation has passed.");

        invitation.Status = ProjectInvitationStatuses.Cancelled;
        invitation.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(invitation);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
