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
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(TransferObjectiveHeadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("An achieved milestone's head cannot be transferred.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        if (objective.CreatedById == userId)
        {
            var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.NewHeadEmployeeId, ct);
            if (newHeadAssignee is null)
                return Result<ObjectiveChangeOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                var oldHeadEmployeeId = objective.OwnerId;

                objective.OwnerId = request.NewHeadEmployeeId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                // Reporting Manager cascade (design §4): direct children only, one level.
                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = request.NewHeadEmployeeId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.NewHeadEmployeeId, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadEmployeeId, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, newHeadAssignee.UserId, userId, "projects:access", innerCt);

                // Old head keeps whatever other access they have (another milestone, or a direct
                // membership); if none, DeactivateMembershipAsync above already removed their only
                // row, so there's nothing further to do here beyond the check itself (design §3
                // step 6 - the "drop from project entirely" case has no separate action once the
                // one row they had is gone).
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadEmployeeId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
            }, ct);
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new TransferObjectiveRequestPayload(request.NewHeadEmployeeId);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Transfer,
            RequestedById = callerEmployeeId.Value,
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveChangeOutcomeResponse>.Success(
            new ObjectiveChangeOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
