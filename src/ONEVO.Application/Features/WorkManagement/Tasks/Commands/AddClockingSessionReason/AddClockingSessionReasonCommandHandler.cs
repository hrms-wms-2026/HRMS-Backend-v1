using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;

public class AddClockingSessionReasonCommandHandler : IRequestHandler<AddClockingSessionReasonCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;

    public AddClockingSessionReasonCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ITaskClockingSessionRepository sessions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddClockingSessionReasonCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var session = await _sessions.GetTrackedByIdForTenantAsync(tenantId, request.SessionId, ct);
        if (session is null)
            return Result.NotFound("Clocking session not found.");

        if (session.EmployeeId != callerEmployeeId.Value)
            return Result.Forbidden("Only the employee who clocked in can add a note to this session.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            session.Reason = request.Reason.Trim();
            session.UpdatedAt = DateTimeOffset.UtcNow;
            _sessions.Update(session);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
