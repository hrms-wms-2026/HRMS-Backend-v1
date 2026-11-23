using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>
/// Task 21: one permission-version counter per user for all instances, so a permission change made through instance
/// A makes the user's older sessions stale on instance B as well. <c>PermissionVersionMiddleware</c> rejects a
/// session only when its version is lower than the current one, so a Redis restart (counters back to 0) never
/// rejects a valid session; it only forgets changes made before the restart, as a process restart does with the
/// in-memory counter.
/// </summary>
public sealed class RedisPermissionVersionService(IConnectionMultiplexer redis, ISessionTicketCache ticketCache) : IPermissionVersionService
{
    private static RedisKey Key(Guid userId) => $"{SharedStateRegistration.KeyPrefix}perm-ver:{userId:N}";

    public async Task<long> GetCurrentVersionAsync(Guid userId, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(userId));
        return value.TryParse(out long version) ? version : 0;
    }

    public async Task<long> IncrementVersionAsync(Guid userId, CancellationToken ct = default)
    {
        var version = await redis.GetDatabase().StringIncrementAsync(Key(userId));
        // A version bump means this user's cached tickets are stale - on every instance (broadcast by the cache).
        ticketCache.EvictUser(userId);
        return version;
    }
}
