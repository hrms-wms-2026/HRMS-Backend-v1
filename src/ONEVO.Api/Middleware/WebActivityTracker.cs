using System.Collections.Concurrent;
using System.Security.Claims;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Common.Models;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Last authenticated web-app request per (tenant, user); see <see cref="IWebActivityPresence"/>.
/// Task 21: with Redis configured the time is also shared, at most once per <see cref="SharedWriteInterval"/> per
/// user so requests do not each become a Redis write, and reads take the later of the local and shared times.
/// If Redis cannot be asked, the local time is used.
/// </summary>
public sealed class WebActivityTracker(ONEVO.Infrastructure.SharedState.ISharedPresenceStore? shared = null) : IWebActivityPresence
{
    public static readonly TimeSpan SharedWriteInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<(Guid TenantId, Guid UserId), DateTimeOffset> _lastSeen = new();
    private readonly ConcurrentDictionary<(Guid TenantId, Guid UserId), DateTimeOffset> _lastShared = new();

    public DateTimeOffset? LastSeenAt(Guid tenantId, Guid userId)
    {
        DateTimeOffset? local = _lastSeen.TryGetValue((tenantId, userId), out var seen) ? seen : null;
        var remote = shared?.WebLastSeenAt(tenantId, userId);
        return remote is { } r && (local is null || r > local) ? r : local;
    }

    public void Touch(Guid tenantId, Guid userId, DateTimeOffset now)
    {
        var key = (tenantId, userId);
        _lastSeen[key] = now;
        if (shared is null)
            return;
        if (_lastShared.TryGetValue(key, out var last) && now - last < SharedWriteInterval)
            return;
        _lastShared[key] = now;
        shared.TouchWeb(tenantId, userId, now);
    }
}

/// <summary>
/// Marks the caller as active in the web app. Runs before the request is handled, so the viewer's
/// own employee-list request already counts them as online.
/// </summary>
public sealed class WebActivityTrackingMiddleware
{
    private const string TrayDeviceTokenType = "tray_device";
    private readonly RequestDelegate _next;

    public WebActivityTrackingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, WebActivityTracker tracker, ITenantContext tenantContext, IDateTimeProvider clock)
    {
        var principal = context.User;
        if (tenantContext.ContextMode == TenantContextMode.Tenant
            && principal.Identity?.IsAuthenticated == true
            && principal.FindFirstValue("token_type") != TrayDeviceTokenType
            && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("user_id"), out var userId))
        {
            tracker.Touch(tenantContext.TenantId, userId, clock.UtcNow);
        }

        await _next(context);
    }
}
