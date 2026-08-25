using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<TransferOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
    }

    public async Task<Result<TransferOutcomeResponse>> Handle(TransferObjectiveHeadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TransferOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<TransferOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<TransferOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TransferOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<TransferOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.IsAchieved)
            return Result<TransferOutcomeResponse>.Failure("An achieved milestone's head cannot be transferred.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<TransferOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.NewHeadEmployeeId, ct);
        if (newHeadAssignee is null)
            return Result<TransferOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

        if (objective.CreatedById == userId)
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                var oldHeadEmployeeId = objective.OwnerId;

                objective.OwnerId = request.NewHeadEmployeeId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = request.NewHeadEmployeeId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.NewHeadEmployeeId, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadEmployeeId, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, newHeadAssignee.UserId, userId, "projects:access", innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadEmployeeId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<TransferOutcomeResponse>.Success(new TransferOutcomeResponse(Applied: true, PendingChangeRequest: null, PendingInvitation: null));
            }, ct);
        }

        if (objective.ReportingManagerId is null)
        {
            var pendingForObjective = await _invitations.ListPendingForObjectiveAsync(tenantId, objective.Id, ct);
            if (pendingForObjective.Any(i => i.InviteType == ProjectInvitationTypes.Leader))
                return Result<TransferOutcomeResponse>.Conflict("A leader invitation is already pending for this milestone.");

            var invitation = new ProjectMemberInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = objective.ProjectId,
                ObjectiveId = objective.Id,
                InvitedEmployeeId = newHeadAssignee.Id,
                InviteType = ProjectInvitationTypes.Leader,
                Status = ProjectInvitationStatuses.Pending,
                InvitedById = callerEmployeeId.Value,
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _invitations.AddAsync(invitation, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<TransferOutcomeResponse>.Success(
                new TransferOutcomeResponse(Applied: false, PendingChangeRequest: null, PendingInvitation: ProjectMemberInvitationMapper.ToResponse(invitation)));
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<TransferOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new TransferObjectiveRequestPayload(request.NewHeadEmployeeId);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Transfer,
            RequestedById = callerEmployeeId.Value,
            ReportingManagerId = objective.ReportingManagerId.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<TransferOutcomeResponse>.Success(
            new TransferOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest), PendingInvitation: null));
    }
}
