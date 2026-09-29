using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;

public class ResolveExceptionCommandHandler : IRequestHandler<ResolveExceptionCommand, Result>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IExceptionScopeResolver _scope;
    private readonly IDateTimeProvider _clock;

    public ResolveExceptionCommandHandler(
        IExceptionRepository exceptions, ICurrentUser currentUser, IExceptionScopeResolver scope, IDateTimeProvider clock)
    {
        _exceptions = exceptions;
        _currentUser = currentUser;
        _scope = scope;
        _clock = clock;
    }

    public async Task<Result> Handle(ResolveExceptionCommand request, CancellationToken ct)
    {
        var exception = await _exceptions.GetByIdAsync(_currentUser.TenantId, request.ExceptionId, ct);
        var scope = await _scope.ResolveAsync(
            forAction: true, exception is null ? [] : [exception.EmployeeId], ct);
        if (scope is null)
            return Result.Forbidden("You do not have permission to resolve exceptions.");

        if (exception is null || !scope.CanSee(exception.EmployeeId))
            return Result.NotFound("Exception not found.");
        if (exception.Status is ExceptionStatus.Resolved)
            return Result.Conflict("Exception is already resolved.");

        exception.Status = ExceptionStatus.Resolved;
        exception.ResolvedAt = _clock.UtcNow;
        exception.ResolvedById = _currentUser.UserId;
        if (!string.IsNullOrWhiteSpace(request.Note))
            exception.ResolutionNote = request.Note.Trim();
        _exceptions.Update(exception);
        await _exceptions.SaveChangesAsync(ct);

        return Result.Success();
    }
}
