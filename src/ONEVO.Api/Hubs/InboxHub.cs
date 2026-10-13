using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ONEVO.Api.Hubs;

/// <summary>
/// Server → browser push for the Inbox and its conversations. Browsers connect with the normal tenant
/// session cookie (default TenantScheme), so no token travels in the query string. Each connection joins
/// only its own user group; every event is addressed to the recipients' user groups, which means a
/// conversation's events reach its current participants and nobody else (no client-joinable channels).
/// </summary>
[Authorize]
public sealed class InboxHub : Hub
{
    public const string MessageCreated = "messageCreated";
    public const string ParticipantAdded = "participantAdded";
    public const string InboxChanged = "inboxChanged";

    public static string UserGroup(Guid tenantId, Guid userId) => $"inbox:{tenantId:D}:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (!Guid.TryParse(user?.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty
            || !Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId == Guid.Empty)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId, userId));
        await base.OnConnectedAsync();
    }
}
