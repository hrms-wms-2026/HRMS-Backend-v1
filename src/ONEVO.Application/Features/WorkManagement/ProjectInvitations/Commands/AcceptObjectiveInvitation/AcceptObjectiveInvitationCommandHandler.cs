using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;

public class AcceptObjectiveInvitationCommandHandler : IRequestHandler<AcceptObjectiveInvitationCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProjectRepository _projects;
    private readonly IOutboxWriter _outboxWriter;

    public AcceptObjectiveInvitationCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectMemberInvitationRepository invitations,
        IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant,
        IUnitOfWork unitOfWork, IProjectRepository projects, IOutboxWriter outboxWriter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _invitations = invitations;
        _objectives = objectives;
        _membership = membership;
        _autoGrant = autoGrant;
        _unitOfWork = unitOfWork;
        _projects = projects;
        _outboxWriter = outboxWriter;
    }

    public async Task<Result> Handle(AcceptObjectiveInvitationCommand request, CancellationToken ct)
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
            return Result.Forbidden("Only the invited employee can accept this invitation.");

        if (invitation.Status != ProjectInvitationStatuses.Pending)
            return Result.Conflict("This invitation has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, invitation.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot accept an invitation on an achieved milestone.");

        var invitee = await _membership.GetActiveAssigneeAsync(tenantId, invitation.InvitedEmployeeId, ct);
        if (invitee is null)
            return Result.Failure("The invited employee must still be active in this tenant.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            if (invitation.InviteType == ProjectInvitationTypes.Leader)
            {
                var oldHeadEmployeeId = objective.OwnerId;
                objective.OwnerId = invitation.InvitedEmployeeId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = invitation.InvitedEmployeeId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, invitation.InvitedEmployeeId, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadEmployeeId, innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadEmployeeId, objective.Id, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, invitee.UserId, userId, "projects:access", innerCt);
            }
            else
            {
                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, invitation.InvitedEmployeeId, innerCt);
            }

            invitation.Status = ProjectInvitationStatuses.Accepted;
            invitation.DecidedAt = now;
            _invitations.Update(invitation);

            if (objective.IsDefault)
            {
                var inviter = await _membership.GetActiveAssigneeAsync(tenantId, invitation.InvitedById, innerCt);
                if (inviter is not null)
                {
                    var project = await _projects.GetByIdForTenantAsync(tenantId, invitation.ProjectId, innerCt);
                    if (project is not null)
                    {
                        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
                            tenantId, [invitation.InvitedEmployeeId], innerCt);
                        var accepterDisplayName = names.GetValueOrDefault(invitation.InvitedEmployeeId) ?? "A teammate";
                        await _outboxWriter.EnqueueAsync(
                            OutboxMessageTypes.WorkNotification,
                            new WorkNotificationPayload(
                                tenantId,
                                inviter.UserId,
                                "work_project_member_accepted",
                                new Dictionary<string, string>
                                {
                                    ["accepterName"] = accepterDisplayName,
                                    ["projectName"] = project.Name
                                },
                                "project_member_invitation",
                                invitation.Id),
                            tenantId,
                            innerCt);
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result.Success();
        }, ct);
    }
}
