using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;

public class DeactivateLeaveTypeCommandHandler : IRequestHandler<DeactivateLeaveTypeCommand, Result>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;

    public DeactivateLeaveTypeCommandHandler(ILeaveTypeRepository leaveTypes, ICurrentUser currentUser)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeactivateLeaveTypeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var entity = await _leaveTypes.GetByIdAsync(tenantId, request.LeaveTypeId, ct);
        if (entity is null)
            return Result.NotFound("Leave type not found.");

        var pendingCount = await _leaveTypes.CountPendingRequestsAsync(tenantId, request.LeaveTypeId, ct);
        if (pendingCount > 0 && !request.Confirmed)
        {
            return Result.Conflict(
                $"There are {pendingCount} pending requests for this leave type.");
        }

        entity.IsActive = false;
        _leaveTypes.Update(entity);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result.Success();
    }
}
