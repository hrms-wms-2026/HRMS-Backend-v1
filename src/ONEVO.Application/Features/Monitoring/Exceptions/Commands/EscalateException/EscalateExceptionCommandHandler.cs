using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;

public class EscalateExceptionCommandHandler : IRequestHandler<EscalateExceptionCommand, Result>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IExceptionScopeResolver _scope;
    private readonly IExceptionAlertRouter _alerts;
    private readonly IDateTimeProvider _clock;

    public EscalateExceptionCommandHandler(
        IExceptionRepository exceptions,
        ICurrentUser currentUser,
        IExceptionScopeResolver scope,
        IExceptionAlertRouter alerts,
        IDateTimeProvider clock)
    {
        _exceptions = exceptions;
        _currentUser = currentUser;
        _scope = scope;
        _alerts = alerts;
        _clock = clock;
    }

    public async Task<Result> Handle(EscalateExceptionCommand request, CancellationToken ct)
    {
        var exception = await _exceptions.GetByIdAsync(_currentUser.TenantId, request.ExceptionId, ct);
        var scope = await _scope.ResolveAsync(
            forAction: true, exception is null ? [] : [exception.EmployeeId], ct);
        if (scope is null)
            return Result.Forbidden("You do not have permission to escalate exceptions.");

        if (exception is null || !scope.CanSee(exception.EmployeeId))
            return Result.NotFound("Exception not found.");
        if (exception.Status is ExceptionStatus.Resolved)
            return Result.Conflict("Exception is already resolved.");
        if (exception.Status is ExceptionStatus.Escalated)
            return Result.Conflict("Exception is already escalated to HR.");

        exception.Status = ExceptionStatus.Escalated;
        exception.EscalatedAt = _clock.UtcNow;
        exception.EscalatedById = _currentUser.UserId;
        if (!string.IsNullOrWhiteSpace(request.Note))
            exception.ResolutionNote = request.Note.Trim();
        _exceptions.Update(exception);

        // Queued on the same unit of work, so the HR alert commits with the escalation.
        await _alerts.NotifyEscalatedAsync(exception, ct);
        await _exceptions.SaveChangesAsync(ct);

        return Result.Success();
    }
}
