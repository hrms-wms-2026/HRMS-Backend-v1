using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUser _currentUser;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, ICurrentUser currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation(
            "Handling {RequestName} for user {UserId} in tenant {TenantId}",
            requestName,
            _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
            _currentUser.IsAuthenticated ? _currentUser.TenantId : Guid.Empty);

        var response = await next();

        _logger.LogInformation("Handled {RequestName}", requestName);

        return response;
    }
}
