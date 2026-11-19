using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Api.Hubs;

/// <summary>
/// Server → tray push. Agents connect with a tray device JWT.
/// </summary>
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class AgentCommandsHub(
    TrayConnectionTracker connections,
    ITrayActivationRepository devices,
    ILogger<AgentCommandsHub> logger) : Hub
{
    public const string RefreshPolicy = "RefreshPolicy";

    private const string TrackedKey = "tray-presence-tracked";

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";

    public static string UserGroup(Guid tenantId, Guid userId) => $"user:{tenantId:D}:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var deviceId))
        {
            Context.Abort();
            return;
        }

        // The connection counts as tray presence for web access, so hold it to the same bar as
        // the HTTP heartbeat: an active registration belonging to this user. A deactivated or
        // replaced device's still-valid JWT must not keep the user "present". (Validity is
        // re-checked on every reconnect; CloseOnAuthenticationExpiration bounds a live one.)
        var device = await devices.FindActiveDeviceAsync(deviceId, tenantId, Context.ConnectionAborted);
        if (device is null || device.UserId != userId)
        {
            logger.LogInformation("Agent hub connection refused — device {DeviceId} is not active", deviceId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId, userId));
        connections.Connected(tenantId, userId);
        Context.Items[TrackedKey] = true;

        // Tells the tray this backend pushes changes, so it may switch to slow fallback polls.
        // An older backend never sends this and the tray keeps its fast polling.
        await Clients.Caller.SendAsync(TrayPushMessages.PushReady);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.ContainsKey(TrackedKey) && TryGetIdentity(out var tenantId, out var userId, out _))
            connections.Disconnected(tenantId, userId);

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetIdentity(out Guid tenantId, out Guid userId, out Guid deviceId)
    {
        userId = Guid.Empty;
        deviceId = Guid.Empty;
        var user = Context.User;
        // The tray JWT's "sub" is the device registration id; inbound claim mapping may expose it
        // as NameIdentifier (TrayCurrentDeviceService reads it the same way).
        var sub = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
        return Guid.TryParse(user?.FindFirstValue("tenant_id"), out tenantId) && tenantId != Guid.Empty
            && Guid.TryParse(user?.FindFirstValue("user_id"), out userId) && userId != Guid.Empty
            && Guid.TryParse(sub, out deviceId) && deviceId != Guid.Empty;
    }
}
