using System.Collections.Concurrent;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Infrastructure.SharedState;

namespace ONEVO.Api.Hubs;

/// <summary>
/// Live agent-hub connections, one entry per connection (a reconnect can open the new connection before the old one's
/// disconnect is processed, so presence lasts until the last connection closes).
/// <para>
/// Task 21: with Redis configured, each connection is also written to <see cref="ISharedPresenceStore"/> and
/// refreshed by <see cref="TrayPresenceRefresher"/>, so a tray connected to instance A counts as present on
/// instance B. If Redis cannot be asked, the answer is "not connected here": TrayPresenceEnforcementMiddleware then
/// falls back to the device's persisted HTTP heartbeat (LastSeenAt within the grace period), which also covers
/// trays connected to another instance - so a Redis outage neither blocks those users nor lets anyone skip the check.
/// </para>
/// </summary>
public sealed class TrayConnectionTracker(ISharedPresenceStore? shared = null) : ITrayConnectionPresence
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid UserId), ConcurrentDictionary<string, byte>> _byUser = new();

    public bool IsConnected(Guid tenantId, Guid userId)
    {
        if (_byUser.TryGetValue((tenantId, userId), out var local) && !local.IsEmpty)
            return true;
        return shared?.IsTrayConnected(tenantId, userId) == true;
    }

    public void Connected(Guid tenantId, Guid userId, string connectionId)
    {
        _byUser.GetOrAdd((tenantId, userId), _ => new ConcurrentDictionary<string, byte>())[connectionId] = 0;
        shared?.TrayConnected(tenantId, userId, connectionId);
    }

    public void Disconnected(Guid tenantId, Guid userId, string connectionId)
    {
        if (_byUser.TryGetValue((tenantId, userId), out var local))
            local.TryRemove(connectionId, out _);
        shared?.TrayDisconnected(tenantId, userId, connectionId);
    }

    /// <summary>This instance's live connections, for the shared-presence refresh.</summary>
    public IReadOnlyList<(Guid TenantId, Guid UserId, string ConnectionId)> LocalConnections()
        => _byUser.SelectMany(user => user.Value.Keys.Select(id => (user.Key.TenantId, user.Key.UserId, id))).ToList();
}

/// <summary>Keeps this instance's tray connections alive in the shared presence store (Redis only).</summary>
public sealed class TrayPresenceRefresher(TrayConnectionTracker tracker, ISharedPresenceStore shared) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RedisPresenceStore.TrayRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                shared.RefreshTrayConnections(tracker.LocalConnections());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
