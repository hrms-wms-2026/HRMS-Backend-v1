using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>
/// Presence seen by every instance (task 21). Used by the API's tray and web trackers only when Redis is configured;
/// they keep their own local view either way. Calls are synchronous because the presence interfaces are, and they
/// are single, small Redis commands.
/// </summary>
public interface ISharedPresenceStore
{
    void TrayConnected(Guid tenantId, Guid userId, string connectionId);
    void TrayDisconnected(Guid tenantId, Guid userId, string connectionId);

    /// <summary>Extends the entries of this instance's live connections (called by a timer).</summary>
    void RefreshTrayConnections(IEnumerable<(Guid TenantId, Guid UserId, string ConnectionId)> connections);

    /// <summary>Null when Redis could not be asked: the caller decides how to fail.</summary>
    bool? IsTrayConnected(Guid tenantId, Guid userId);

    void TouchWeb(Guid tenantId, Guid userId, DateTimeOffset at);

    /// <summary>Null when unknown or when Redis could not be asked.</summary>
    DateTimeOffset? WebLastSeenAt(Guid tenantId, Guid userId);
}

/// <summary>
/// Tray: one sorted set per user, one member per hub connection, scored with the time the entry stops counting.
/// Counting members with a score in the future means a crashed instance's connections drop out by themselves after
/// <see cref="TrayEntryLifetime"/> - no counter is ever left behind. Web: one key per user holding the last request
/// time, expiring after <see cref="WebEntryLifetime"/>.
/// </summary>
public sealed class RedisPresenceStore(
    IConnectionMultiplexer redis, ILogger<RedisPresenceStore> logger, TimeSpan? trayEntryLifetime = null) : ISharedPresenceStore
{
    private readonly TimeSpan _trayEntryLifetime = trayEntryLifetime ?? TrayEntryLifetime;

    /// <summary>Three refresh periods: one missed refresh never drops a live connection.</summary>
    public static readonly TimeSpan TrayEntryLifetime = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan TrayRefreshInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WebEntryLifetime = TimeSpan.FromHours(1);

    private static RedisKey TrayKey(Guid tenantId, Guid userId) => $"{SharedStateRegistration.KeyPrefix}tray:{tenantId:N}:{userId:N}";
    private static RedisKey WebKey(Guid tenantId, Guid userId) => $"{SharedStateRegistration.KeyPrefix}web:{tenantId:N}:{userId:N}";
    private static double Score(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    public void TrayConnected(Guid tenantId, Guid userId, string connectionId)
        => Try("tray connect", () => Upsert(redis.GetDatabase(), tenantId, userId, connectionId));

    public void TrayDisconnected(Guid tenantId, Guid userId, string connectionId)
        => Try("tray disconnect", () =>
            redis.GetDatabase().SortedSetRemove(TrayKey(tenantId, userId), connectionId, CommandFlags.FireAndForget));

    public void RefreshTrayConnections(IEnumerable<(Guid TenantId, Guid UserId, string ConnectionId)> connections)
        => Try("tray refresh", () =>
        {
            var db = redis.GetDatabase();
            foreach (var (tenantId, userId, connectionId) in connections)
                Upsert(db, tenantId, userId, connectionId);
        });

    public bool? IsTrayConnected(Guid tenantId, Guid userId)
    {
        try
        {
            var now = Score(DateTimeOffset.UtcNow);
            return redis.GetDatabase().SortedSetLength(TrayKey(tenantId, userId), now, double.PositiveInfinity, Exclude.Start) > 0;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis tray presence lookup failed.");
            return null;
        }
    }

    public void TouchWeb(Guid tenantId, Guid userId, DateTimeOffset at)
        => Try("web touch", () => redis.GetDatabase().StringSet(
            WebKey(tenantId, userId), at.ToUnixTimeMilliseconds(), WebEntryLifetime, flags: CommandFlags.FireAndForget));

    public DateTimeOffset? WebLastSeenAt(Guid tenantId, Guid userId)
    {
        try
        {
            var value = redis.GetDatabase().StringGet(WebKey(tenantId, userId));
            return value.TryParse(out long ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis web presence lookup failed.");
            return null;
        }
    }

    private void Upsert(IDatabase db, Guid tenantId, Guid userId, string connectionId)
    {
        var key = TrayKey(tenantId, userId);
        var now = DateTimeOffset.UtcNow;
        db.SortedSetAdd(key, connectionId, Score(now + _trayEntryLifetime), CommandFlags.FireAndForget);
        // Old members of crashed instances: dropped when they stop counting, and the key goes with the last one.
        db.SortedSetRemoveRangeByScore(key, double.NegativeInfinity, Score(now), flags: CommandFlags.FireAndForget);
        db.KeyExpire(key, _trayEntryLifetime, CommandFlags.FireAndForget);
    }

    private void Try(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis presence {Operation} failed; this instance's local view is still used.", operation);
        }
    }
}
