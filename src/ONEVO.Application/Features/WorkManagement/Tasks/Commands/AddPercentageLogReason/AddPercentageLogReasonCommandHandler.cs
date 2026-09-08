using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason;

public class AddPercentageLogReasonCommandHandler : IRequestHandler<AddPercentageLogReasonCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskPercentageLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public AddPercentageLogReasonCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ITaskPercentageLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddPercentageLogReasonCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var log = await _logs.GetTrackedByIdForTenantAsync(tenantId, request.LogId, ct);
        if (log is null)
            return Result.NotFound("Percentage log not found.");

        if (log.EmployeeId != callerEmployeeId.Value)
            return Result.Forbidden("Only the employee who created this percentage log can add a note.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            log.Reason = request.Reason.Trim();
            log.UpdatedAt = DateTimeOffset.UtcNow;
            _logs.Update(log);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
