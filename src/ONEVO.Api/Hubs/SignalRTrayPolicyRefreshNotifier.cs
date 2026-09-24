using Microsoft.AspNetCore.SignalR;
using ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;

namespace ONEVO.Api.Hubs;

public sealed class SignalRTrayPolicyRefreshNotifier(
    IHubContext<AgentCommandsHub> hub,
    ILogger<SignalRTrayPolicyRefreshNotifier> logger) : ITrayPolicyRefreshNotifier
{
    public async Task NotifyTenantAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return;

        try
        {
            await hub.Clients.Group(AgentCommandsHub.TenantGroup(tenantId))
                .SendAsync(AgentCommandsHub.RefreshPolicy, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tray policy refresh notify failed for tenant {TenantId}", tenantId);
        }
    }
}
