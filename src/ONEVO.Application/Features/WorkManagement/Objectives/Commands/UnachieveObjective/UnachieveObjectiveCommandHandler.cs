using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;

public class UnachieveObjectiveCommandHandler : IRequestHandler<UnachieveObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public UnachieveObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(UnachieveObjectiveCommand request, CancellationToken ct)
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
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (!objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("Objective is not achieved.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can un-achieve it.");

        if (objective.CreatedById == userId)
        {
            var headAssignee = await _membership.GetActiveAssigneeAsync(tenantId, objective.OwnerId, ct);
            if (headAssignee is null)
                return Result<ObjectiveChangeOutcomeResponse>.Failure("The current head must be an active employee in this tenant.");

            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                objective.IsAchieved = false;
                objective.AchievedAt = null;
                objective.UpdatedAt = DateTimeOffset.UtcNow;
                _objectives.Update(objective);

                // Un-freezing restores the Head's active participation, mirroring Achieve's own
                // cleanup in reverse.
                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);

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
            RequestType = ObjectiveChangeRequestTypes.Unachieve,
            RequestedById = callerEmployeeId.Value,
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
