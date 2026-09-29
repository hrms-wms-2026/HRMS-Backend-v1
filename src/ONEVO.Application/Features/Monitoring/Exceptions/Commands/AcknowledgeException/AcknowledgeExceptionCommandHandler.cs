using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;

public class AcknowledgeExceptionCommandHandler : IRequestHandler<AcknowledgeExceptionCommand, Result>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IExceptionScopeResolver _scope;
    private readonly IDateTimeProvider _clock;

    public AcknowledgeExceptionCommandHandler(
        IExceptionRepository exceptions, ICurrentUser currentUser, IExceptionScopeResolver scope, IDateTimeProvider clock)
    {
        _exceptions = exceptions;
        _currentUser = currentUser;
        _scope = scope;
        _clock = clock;
    }

    public async Task<Result> Handle(AcknowledgeExceptionCommand request, CancellationToken ct)
    {
        var exception = await _exceptions.GetByIdAsync(_currentUser.TenantId, request.ExceptionId, ct);
        var scope = await _scope.ResolveAsync(
            forAction: true, exception is null ? [] : [exception.EmployeeId], ct);
        if (scope is null)
            return Result.Forbidden("You do not have permission to acknowledge exceptions.");

        // Out-of-scope cases answer the same as missing ones so ids can't be probed.
        if (exception is null || !scope.CanSee(exception.EmployeeId))
            return Result.NotFound("Exception not found.");
        if (exception.Status is ExceptionStatus.Resolved)
            return Result.Conflict("Exception is already resolved.");
        if (exception.Status is ExceptionStatus.Acknowledged)
            return Result.Conflict("Exception is already acknowledged.");
        // Acknowledging would silently pull an escalated case back out of HR's queue.
        if (exception.Status is ExceptionStatus.Escalated)
            return Result.Conflict("Exception is escalated to HR - resolve it instead.");

        exception.Status = ExceptionStatus.Acknowledged;
        exception.AcknowledgedAt = _clock.UtcNow;
        exception.AcknowledgedById = _currentUser.UserId;
        _exceptions.Update(exception);
        await _exceptions.SaveChangesAsync(ct);

        return Result.Success();
    }
}
