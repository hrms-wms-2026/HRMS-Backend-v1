using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Common.Behaviors;

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly int _thresholdMs;

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
        ICurrentUser currentUser,
        IOptions<PerformanceOptions> options)
    {
        _logger = logger;
        _currentUser = currentUser;
        _thresholdMs = options.Value.SlowRequestThresholdMs;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next(ct);
        sw.Stop();

        if (sw.ElapsedMilliseconds > _thresholdMs)
        {
            _logger.LogWarning(
                "Slow request: {RequestName} took {Elapsed}ms (threshold {ThresholdMs}ms) for user {UserId} in tenant {TenantId}",
                typeof(TRequest).Name,
                sw.ElapsedMilliseconds,
                _thresholdMs,
                _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
                _currentUser.IsAuthenticated ? _currentUser.TenantId : Guid.Empty);
        }

        return response;
    }
}
