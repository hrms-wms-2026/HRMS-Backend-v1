using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Features.DevPlatform.Tenancy.ServiceInterfaces;
using ONEVO.Infrastructure.SharedState;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

/// <summary>Evicts the tenant-by-slug entry here and, through the shared bus, on every other instance (task 21).</summary>
public sealed class TenantCacheInvalidator : ITenantCacheInvalidator
{
    private readonly IMemoryCache _cache;
    private readonly ISharedInvalidationBus _bus;

    public TenantCacheInvalidator(IMemoryCache cache, ISharedInvalidationBus bus)
    {
        _cache = cache;
        _bus = bus;
    }

    public void InvalidateBySlug(string slug)
    {
        _cache.Remove(MemoryCacheInvalidationHandler.TenantSlugCacheKey(slug));
        _bus.Publish(new Invalidation(InvalidationKinds.TenantSlug, slug));
    }
}
