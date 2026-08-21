using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;

public class ResolveExceptionCommandHandler : IRequestHandler<ResolveExceptionCommand, Result>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ResolveExceptionCommandHandler(
        IExceptionRepository exceptions, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _exceptions = exceptions;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(ResolveExceptionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result.Forbidden("Authentication required.");
        if (!_currentUser.HasPermission("exceptions:acknowledge"))
            return Result.Forbidden("You do not have permission to resolve exceptions.");

        var exception = await _exceptions.GetByIdAsync(_currentUser.TenantId, request.ExceptionId, ct);
        if (exception is null)
            return Result.NotFound("Exception not found.");

        exception.Status = ExceptionStatus.Resolved;
        exception.ResolvedAt = _clock.UtcNow;
        exception.ResolvedById = _currentUser.UserId;
        _exceptions.Update(exception);
        await _exceptions.SaveChangesAsync(ct);

        return Result.Success();
    }
}
