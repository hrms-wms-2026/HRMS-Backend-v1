using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Common.Behaviors;

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUser _currentUser;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger, ICurrentUser currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "Slow request: {RequestName} took {Elapsed}ms for user {UserId} in tenant {TenantId}",
                typeof(TRequest).Name,
                sw.ElapsedMilliseconds,
                _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
                _currentUser.IsAuthenticated ? _currentUser.TenantId : Guid.Empty);
        }

        return response;
    }
}
