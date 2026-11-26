using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

/// <summary>
/// Reloads the rate limit provider (task 46) on this API instance after a rate limit command saved a
/// change. It is registered outside <c>AuditingBehavior</c>, which runs the command and its audit row in
/// one transaction: a reload inside that transaction would read the old committed rows
/// (READ_COMMITTED_SNAPSHOT) and the change would only arrive with the 30-second check.
/// </summary>
public sealed class RateLimitReloadBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly RateLimitReloadSignal _signal;
    private readonly IRateLimitSettingsInvalidator _invalidator;
    private readonly ILogger<RateLimitReloadBehavior<TRequest, TResponse>> _logger;

    public RateLimitReloadBehavior(
        RateLimitReloadSignal signal,
        IRateLimitSettingsInvalidator invalidator,
        ILogger<RateLimitReloadBehavior<TRequest, TResponse>> logger)
    {
        _signal = signal;
        _invalidator = invalidator;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not (ISetRateLimitCommand or IRemoveRateLimitCommand))
            return await next(ct);

        var response = await next(ct);
        if (!_signal.Requested)
            return response;

        try
        {
            // Not the request token: once saved, the change must reach the limiter even if the caller left.
            await _invalidator.InvalidateAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The change is saved; the 30-second check picks it up.
            _logger.LogWarning(ex, "Rate limit settings were saved but the immediate reload failed.");
        }

        return response;
    }
}
