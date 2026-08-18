using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;

public class RejectObjectiveChangeRequestCommandHandler : IRequestHandler<RejectObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RejectObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveChangeRequestRepository changeRequests,
        IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _changeRequests = changeRequests;
        _objectives = objectives;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectObjectiveChangeRequestCommand request, CancellationToken ct)
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

        var changeRequest = await _changeRequests.GetByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (changeRequest is null)
            return Result.NotFound("Change request not found.");

        if (changeRequest.ReportingManagerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this request's reporting manager can reject it.");

        if (changeRequest.Status != ObjectiveChangeRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        changeRequest.Status = ObjectiveChangeRequestStatuses.Rejected;
        changeRequest.DecidedAt = DateTimeOffset.UtcNow;
        changeRequest.DecidedById = userId;
        _changeRequests.Update(changeRequest);

        if (changeRequest.RequestType == ObjectiveChangeRequestTypes.ExtendAllocation)
        {
            var objective = await _objectives.GetByIdForTenantAsync(tenantId, changeRequest.ObjectiveId, ct);
            var requester = await _membership.GetActiveAssigneeAsync(tenantId, changeRequest.RequestedById, ct);
            if (objective is not null && requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, requester.UserId, "work_allocation_extend_request_decided",
                    new Dictionary<string, string>
                    {
                        ["decision"] = "rejected",
                        ["objectiveName"] = objective.Title
                    },
                    "objective_change_request", changeRequest.Id, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
