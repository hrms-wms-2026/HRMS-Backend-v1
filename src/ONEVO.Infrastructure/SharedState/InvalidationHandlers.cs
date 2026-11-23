using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Infrastructure.Services.SharedPlatform;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>
/// The <see cref="ISessionTicketCache"/> every caller gets (the save interceptor, permission changes, sign-out):
/// evicts this instance's cached tickets, then tells the other instances to do the same, so a revoked session or a
/// changed permission is refused everywhere within one message, not after the 30-second ticket cache lifetime.
/// </summary>
public sealed class BroadcastingSessionTicketCache(SessionTicketCache local, ISharedInvalidationBus bus) : ISessionTicketCache
{
    public void EvictSession(string keyHash)
    {
        local.EvictSession(keyHash);
        bus.Publish(new Invalidation(InvalidationKinds.Session, keyHash));
    }

    public void EvictUser(Guid userId)
    {
        local.EvictUser(userId);
        bus.Publish(new Invalidation(InvalidationKinds.User, userId.ToString("N")));
    }

    public void EvictTenant(Guid tenantId)
    {
        local.EvictTenant(tenantId);
        bus.Publish(new Invalidation(InvalidationKinds.Tenant, tenantId.ToString("N")));
    }

    public void EvictAll()
    {
        local.EvictAll();
        bus.Publish(new Invalidation(InvalidationKinds.AllSessions));
    }
}

/// <summary>Applies session evictions published by other instances to this instance's ticket cache.</summary>
public sealed class SessionTicketInvalidationHandler(SessionTicketCache local) : IInvalidationHandler
{
    public void Handle(Invalidation invalidation)
    {
        switch (invalidation.Kind)
        {
            case InvalidationKinds.Session when invalidation.Key is { } keyHash:
                local.EvictSession(keyHash);
                break;
            case InvalidationKinds.User when Guid.TryParse(invalidation.Key, out var userId):
                local.EvictUser(userId);
                break;
            case InvalidationKinds.Tenant when Guid.TryParse(invalidation.Key, out var tenantId):
                local.EvictTenant(tenantId);
                break;
            case InvalidationKinds.AllSessions or InvalidationKinds.Everything:
                local.EvictAll();
                break;
        }
    }
}

/// <summary>Tenant-by-slug entries (HostTenantResolutionMiddleware) and <see cref="ICacheService"/> entries.</summary>
public sealed class MemoryCacheInvalidationHandler(IMemoryCache cache, MemoryCacheService cacheService) : IInvalidationHandler
{
    public void Handle(Invalidation invalidation)
    {
        switch (invalidation.Kind)
        {
            case InvalidationKinds.TenantSlug when invalidation.Key is { } slug:
                cache.Remove(TenantSlugCacheKey(slug));
                break;
            case InvalidationKinds.CacheKey when invalidation.Key is { } key:
                cacheService.RemoveLocal(key);
                break;
            case InvalidationKinds.CachePrefix when invalidation.Key is { } prefix:
                cacheService.RemoveByPrefixLocal(prefix);
                break;
            case InvalidationKinds.Everything:
                // Rare (a lost Redis connection): drop the whole process cache rather than guess what was missed.
                (cache as MemoryCache)?.Compact(1.0);
                cacheService.RemoveByPrefixLocal(string.Empty);
                break;
        }
    }

    public static string TenantSlugCacheKey(string slug) => $"tenant:slug:{slug}";
}
