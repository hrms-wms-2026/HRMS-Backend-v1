using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;

public class AchieveObjectiveCommandHandler : IRequestHandler<AchieveObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IObjectiveChangeRequestRepository changeRequests,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(AchieveObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("Objective is already achieved.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can achieve it.");

        // Precondition (design §6): every direct child must already be achieved. Shallow check -
        // grandchildren are covered transitively, since a child can't itself be achieved until
        // ITS children are.
        var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, ct);
        if (directChildren.Any(c => !c.IsAchieved))
            return Result<ObjectiveChangeOutcomeResponse>.Failure("All sub-milestones must be achieved before this one can be.");

        if (objective.CreatedById == userId)
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                objective.IsAchieved = true;
                objective.AchievedAt = now;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                // Freezing drops the Head's active participation on this milestone (design §6) -
                // same outgoing-access pattern as Transfer step 6, just with no new Head to
                // upsert a membership for.
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, objective.OwnerId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
            }, ct);
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Achieve,
            RequestedById = userId,
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = null,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveChangeOutcomeResponse>.Success(
            new ObjectiveChangeOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
