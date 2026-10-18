using System.Collections.Concurrent;
using System.Security.Claims;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Common.Models;

namespace ONEVO.Api.Middleware;

/// <summary>Last authenticated web-app request per (tenant, user); see <see cref="IWebActivityPresence"/>.</summary>
public sealed class WebActivityTracker : IWebActivityPresence
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid UserId), DateTimeOffset> _lastSeen = new();

    public DateTimeOffset? LastSeenAt(Guid tenantId, Guid userId) =>
        _lastSeen.TryGetValue((tenantId, userId), out var seen) ? seen : null;

    public void Touch(Guid tenantId, Guid userId, DateTimeOffset now) => _lastSeen[(tenantId, userId)] = now;
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
