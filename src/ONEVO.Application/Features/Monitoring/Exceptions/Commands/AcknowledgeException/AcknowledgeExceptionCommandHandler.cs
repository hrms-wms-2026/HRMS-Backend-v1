using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;

public class AcknowledgeExceptionCommandHandler : IRequestHandler<AcknowledgeExceptionCommand, Result>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AcknowledgeExceptionCommandHandler(
        IExceptionRepository exceptions, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _exceptions = exceptions;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(AcknowledgeExceptionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result.Forbidden("Authentication required.");
        if (!_currentUser.HasPermission("exceptions:acknowledge"))
            return Result.Forbidden("You do not have permission to acknowledge exceptions.");

        var exception = await _exceptions.GetByIdAsync(_currentUser.TenantId, request.ExceptionId, ct);
        if (exception is null)
            return Result.NotFound("Exception not found.");
        if (exception.Status is ExceptionStatus.Resolved)
            return Result.Conflict("Exception is already resolved.");

        exception.Status = ExceptionStatus.Acknowledged;
        exception.AcknowledgedAt = _clock.UtcNow;
        exception.AcknowledgedById = _currentUser.UserId;
        _exceptions.Update(exception);
        await _exceptions.SaveChangesAsync(ct);

        return Result.Success();
    }
}
