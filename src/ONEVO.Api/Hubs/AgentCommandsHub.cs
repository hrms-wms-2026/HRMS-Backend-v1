using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ONEVO.Api.Hubs;

/// <summary>
/// Server → tray push. Agents connect with a tray device JWT.
/// </summary>
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class AgentCommandsHub : Hub
{
    public const string RefreshPolicy = "RefreshPolicy";

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirstValue("tenant_id");
        if (Guid.TryParse(tenantId, out var id) && id != Guid.Empty)
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(id));

        await base.OnConnectedAsync();
    }
}
