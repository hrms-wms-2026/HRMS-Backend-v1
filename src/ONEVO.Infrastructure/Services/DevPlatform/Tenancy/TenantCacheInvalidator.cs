using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Features.DevPlatform.Tenancy.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

public sealed class TenantCacheInvalidator : ITenantCacheInvalidator
{
    private readonly IMemoryCache _cache;

    public TenantCacheInvalidator(IMemoryCache cache) => _cache = cache;

    public void InvalidateBySlug(string slug) =>
        _cache.Remove($"tenant:slug:{slug}");
}
