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

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;

public class DeleteObjectiveCommandHandler : IRequestHandler<DeleteObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public DeleteObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(DeleteObjectiveCommand request, CancellationToken ct)
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
        if (objective is null)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project delete endpoint for the Default Objective.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can delete it.");

        if (!objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("Objective already deleted.");

        if (objective.CreatedById == userId)
        {
            objective.IsActive = false;
            objective.UpdatedAt = DateTimeOffset.UtcNow;
            _objectives.Update(objective);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Delete,
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
