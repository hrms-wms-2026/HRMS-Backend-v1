namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

/// <summary>
/// Per request: set by <see cref="RateLimitAdminService"/> when a rate limit change was saved, read by
/// <see cref="RateLimitReloadBehavior{TRequest, TResponse}"/> once the request's transaction has committed.
/// </summary>
public sealed class RateLimitReloadSignal
{
    public bool Requested { get; private set; }

    public void Request() => Requested = true;
}
