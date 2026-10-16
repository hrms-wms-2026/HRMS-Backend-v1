using System.Collections.Concurrent;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Api.Hubs;

/// <summary>
/// Counts live agent-hub connections per (tenant, user). Counted, not flagged: a reconnect can
/// open the new connection before the old one's disconnect is processed.
/// </summary>
public sealed class TrayConnectionTracker : ITrayConnectionPresence
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid UserId), int> _connections = new();

    public bool IsConnected(Guid tenantId, Guid userId) =>
        _connections.TryGetValue((tenantId, userId), out var count) && count > 0;

    public void Connected(Guid tenantId, Guid userId) =>
        _connections.AddOrUpdate((tenantId, userId), 1, (_, count) => count + 1);

    public void Disconnected(Guid tenantId, Guid userId)
    {
        var key = (tenantId, userId);
        var remaining = _connections.AddOrUpdate(key, 0, (_, count) => Math.Max(0, count - 1));
        if (remaining == 0)
            _connections.TryRemove(new KeyValuePair<(Guid, Guid), int>(key, 0));
    }
}
