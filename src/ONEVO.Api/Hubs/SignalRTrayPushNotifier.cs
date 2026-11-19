using Microsoft.AspNetCore.SignalR;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Api.Hubs;

public sealed class SignalRTrayPushNotifier(
    IHubContext<AgentCommandsHub> hub,
    ILogger<SignalRTrayPushNotifier> logger) : ITrayPushNotifier
{
    public async Task NotifyUserAsync(Guid tenantId, Guid userId, string message, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return;

        try
        {
            await hub.Clients.Group(AgentCommandsHub.UserGroup(tenantId, userId)).SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            // Never fail the triggering operation — the tray's fallback poll catches up.
            logger.LogWarning(ex, "Tray push {Message} failed for user {UserId}", message, userId);
        }
    }
}
