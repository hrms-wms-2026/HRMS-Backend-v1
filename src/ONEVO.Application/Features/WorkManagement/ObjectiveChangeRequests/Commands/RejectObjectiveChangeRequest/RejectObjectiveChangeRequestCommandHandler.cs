using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;

public class RejectObjectiveChangeRequestCommandHandler : IRequestHandler<RejectObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;

    public RejectObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _changeRequests = changeRequests;
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

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
