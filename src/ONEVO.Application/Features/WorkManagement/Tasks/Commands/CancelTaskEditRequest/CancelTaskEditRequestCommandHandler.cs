using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskEditRequest;

public class CancelTaskEditRequestCommandHandler
    : IRequestHandler<CancelTaskEditRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public CancelTaskEditRequestCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ITaskEditRequestRepository requests,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        CancelTaskEditRequestCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(
            tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(
            tenantId, request.RequestId, ct);
        if (pending is null)
            return Result.NotFound("Request not found.");

        if (pending.Status != TaskEditRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        if (pending.RequestedByEmployeeId != callerEmployeeId.Value)
            return Result.Forbidden("Only the requester can cancel this request.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            pending.Status = TaskEditRequestStatuses.Cancelled;
            pending.UpdatedAt = DateTimeOffset.UtcNow;
            _requests.Update(pending);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
