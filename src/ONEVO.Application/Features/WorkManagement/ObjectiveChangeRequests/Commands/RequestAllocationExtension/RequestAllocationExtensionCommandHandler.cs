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
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public class RequestAllocationExtensionCommandHandler : IRequestHandler<RequestAllocationExtensionCommand, Result<ObjectiveChangeRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _requests;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RequestAllocationExtensionCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository requests, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _requests = requests;
        _membership = membership;
        _notifications = notifications;
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
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId.Value], ct);
        var requesterDisplayName = names.GetValueOrDefault(callerEmployeeId.Value) ?? "A teammate";

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

            var manager = await _membership.GetActiveAssigneeAsync(tenantId, objective.ReportingManagerId.Value, innerCt);
            if (manager is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, manager.UserId, "work_allocation_extend_request_created",
                    new Dictionary<string, string>
                    {
                        ["requesterName"] = requesterDisplayName,
                        ["requestedHours"] = payload.RequestedAdditionalHours.ToString("0.##"),
                        ["objectiveName"] = objective.Title
                    },
                    "objective_change_request", entity.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveChangeRequestResponse>.Success(ObjectiveMapper.ToResponse(entity));
        }, ct);
    }
}
