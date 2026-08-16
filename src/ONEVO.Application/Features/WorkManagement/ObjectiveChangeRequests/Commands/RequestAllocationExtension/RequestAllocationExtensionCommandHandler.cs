using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public class RequestAllocationExtensionCommandHandler : IRequestHandler<RequestAllocationExtensionCommand, Result<ObjectiveChangeRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public RequestAllocationExtensionCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository requests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeRequestResponse>> Handle(RequestAllocationExtensionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveChangeRequestResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeRequestResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveChangeRequestResponse>.Forbidden("Only this milestone's owner can request an allocation extension.");

        if (objective.ReportingManagerId is null)
            return Result<ObjectiveChangeRequestResponse>.Failure(
                "This milestone has no Reporting Manager to route to - it is a top-level milestone. Edit the Project directly instead.", 400);

        if (await _requests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeRequestResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new ExtendAllocationRequestPayload(request.RequestedAdditionalHours, request.Reason.Trim());

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new ObjectiveChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ObjectiveId = objective.Id,
                RequestType = ObjectiveChangeRequestTypes.ExtendAllocation,
                RequestedById = callerEmployeeId.Value, ReportingManagerId = objective.ReportingManagerId.Value,
                Status = ObjectiveChangeRequestStatuses.Pending, PayloadJson = JsonSerializer.Serialize(payload),
                CreatedById = userId, CreatedAt = now
            };

            await _requests.AddAsync(entity, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveChangeRequestResponse>.Success(ObjectiveMapper.ToResponse(entity));
        }, ct);
    }
}
