using System.Threading.RateLimiting;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.RateLimiting;

/// <summary>
/// Coarse per-IP flood guard that runs before authentication (setting
/// <c>RateLimiting:PreAuthentication</c>). The identity-partitioned limiter has to run after
/// authentication, which means every forged cookie or bearer token would otherwise be validated
/// before any limit applies. This cuts such floods first. It counts every /api/v1 and /admin/v1
/// request from an IP, so its default is far above what an office of signed-in users sends.
/// </summary>
public sealed class PreAuthenticationRateLimitingMiddleware : IAsyncDisposable
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitSettingsProvider _settings;
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public PreAuthenticationRateLimitingMiddleware(RequestDelegate next, IRateLimitSettingsProvider settings)
    {
        _next = next;
        _settings = settings;
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(Partition);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsLimitedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        using var lease = await _limiter.AcquireAsync(context, 1, context.RequestAborted);
        if (lease.IsAcquired)
        {
            await _next(context);
            return;
        }

        await ApiRateLimitingExtensions.WriteTooManyRequestsAsync(
            context, lease, "PreAuthentication", null, context.RequestAborted);
    }

    private RateLimitPartition<string> Partition(HttpContext context)
    {
        var snapshot = _settings.GetSettings(null);
        if (!snapshot.Settings.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        var limit = snapshot.Settings.PreAuthentication;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter($"preauth:{ip}:v{snapshot.Version}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
            SegmentsPerWindow = limit.SegmentsPerWindow,
            QueueLimit = 0,
        });
    }

    private static bool IsLimitedPath(PathString path) =>
        path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/admin/v1", StringComparison.OrdinalIgnoreCase);

    public ValueTask DisposeAsync() => _limiter.DisposeAsync();
}
