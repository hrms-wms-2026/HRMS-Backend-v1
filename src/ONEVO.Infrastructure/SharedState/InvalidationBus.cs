using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>What to evict on every instance. <see cref="Key"/> is a hash, id or cache key - never personal data.</summary>
public sealed record Invalidation(string Kind, string? Key = null)
{
    public static Invalidation Everything { get; } = new(InvalidationKinds.Everything);
}

public static class InvalidationKinds
{
    public const string Session = "session";
    public const string User = "user";
    public const string Tenant = "tenant";
    public const string AllSessions = "all-sessions";
    public const string TenantSlug = "tenant-slug";
    public const string CacheKey = "cache-key";
    public const string CachePrefix = "cache-prefix";

    /// <summary>Sent locally after a lost Redis connection: messages from other instances may have been missed.</summary>
    public const string Everything = "everything";
}

/// <summary>Evicts this instance's local copy. Must be idempotent: an instance also receives its own messages.</summary>
public interface IInvalidationHandler
{
    void Handle(Invalidation invalidation);
}

/// <summary>
/// Tells the other instances to evict their local cache entries. Callers evict their own local entry first, then
/// publish, and publish only after the database change is saved. Publishing never throws: a lost message only
/// leaves another instance serving its entry until the entry's own (short) lifetime ends.
/// </summary>
public interface ISharedInvalidationBus
{
    void Publish(Invalidation invalidation);
}

/// <summary>Single instance: there is nobody else to tell.</summary>
public sealed class LocalInvalidationBus : ISharedInvalidationBus
{
    public void Publish(Invalidation invalidation)
    {
    }
}

/// <summary>Redis pub/sub on <c>crownus:invalidate</c>. On a restored connection every handler evicts everything.</summary>
public sealed class RedisInvalidationBus(
    IConnectionMultiplexer redis, IServiceProvider services, ILogger<RedisInvalidationBus> logger) : ISharedInvalidationBus, IHostedService
{
    public static readonly RedisChannel Channel = RedisChannel.Literal(SharedStateRegistration.KeyPrefix + "invalidate");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Publish(Invalidation invalidation)
    {
        try
        {
            redis.GetSubscriber().Publish(Channel, JsonSerializer.Serialize(invalidation, Json), CommandFlags.FireAndForget);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish cache invalidation {Kind}; other instances keep their entry until it expires.",
                invalidation.Kind);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionRestored += OnConnectionRestored;
        try
        {
            await redis.GetSubscriber().SubscribeAsync(Channel, (_, message) => Receive(message));
        }
        catch (Exception ex)
        {
            // AbortOnConnectFail=false: the multiplexer re-subscribes when Redis comes back.
            logger.LogWarning(ex, "Could not subscribe to cache invalidations yet; Redis is unreachable.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionRestored -= OnConnectionRestored;
        return Task.CompletedTask;
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        logger.LogWarning("Redis connection restored; evicting every local cache entry in case invalidations were missed.");
        Dispatch(Invalidation.Everything);
    }

    private void Receive(RedisValue message)
    {
        Invalidation? invalidation;
        try
        {
            invalidation = JsonSerializer.Deserialize<Invalidation>(message.ToString(), Json);
        }
        catch (JsonException)
        {
            return;
        }
        if (invalidation is not null)
            Dispatch(invalidation);
    }

    internal void Dispatch(Invalidation invalidation)
    {
        foreach (var handler in services.GetServices<IInvalidationHandler>())
        {
            try
            {
                handler.Handle(invalidation);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache invalidation handler {Handler} failed for {Kind}.", handler.GetType().Name, invalidation.Kind);
            }
        }
    }
}
