using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

/// <summary>
/// <see cref="ITenantCache"/> over the process cache (<see cref="MemoryCacheService"/>), whose removals already reach
/// every instance through the task 21 invalidation bus. Scoped, because whether this request may fill a tenant entry
/// depends on its tenant context.
/// </summary>
public sealed class TenantCache : ITenantCache
{
    private readonly MemoryCacheService _cache;
    private readonly ITenantContext _tenantContext;
    private readonly IOptionsMonitor<ReferenceDataCacheOptions> _options;

    public TenantCache(
        MemoryCacheService cache, ITenantContext tenantContext, IOptionsMonitor<ReferenceDataCacheOptions> options)
    {
        _cache = cache;
        _tenantContext = tenantContext;
        _options = options;
    }

    public Task<T> GetOrCreateAsync<T>(
        Guid tenantId, string name, string variant, Func<CancellationToken, Task<T>> load, CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Enabled || !CanReadTenant(tenantId))
            return load(ct);

        return GetOrLoadAsync(ReferenceDataCacheKeys.ForTenant(tenantId, name, variant), load, ct);
    }

    public Task<T> GetOrCreateGlobalAsync<T>(string name, Func<CancellationToken, Task<T>> load, CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Enabled)
            return load(ct);

        return GetOrLoadAsync(ReferenceDataCacheKeys.Global(name), load, ct);
    }

    public Task InvalidateAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        _cache.RemoveByPrefixAsync(ReferenceDataCacheKeys.TenantPrefix(tenantId, name), ct);

    public Task InvalidateEverywhereAsync(string name, CancellationToken ct = default) =>
        _cache.RemoveByPrefixAsync(ReferenceDataCacheKeys.NamePrefix(name), ct);

    /// <summary>
    /// Tenant mode reads only its own tenant's rows (query filter and RLS); admin mode reads every tenant's rows; system
    /// mode reads no tenant rows. Anything but a matching read would cache an empty or partial result for that tenant,
    /// so those requests go straight to the database instead.
    /// </summary>
    private bool CanReadTenant(Guid tenantId) =>
        tenantId != Guid.Empty
        && (_tenantContext.ContextMode == TenantContextMode.Admin
            || (_tenantContext.ContextMode == TenantContextMode.Tenant && _tenantContext.TenantId == tenantId));

    private async Task<T> GetOrLoadAsync<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        var hit = await _cache.GetAsync<Entry<T>>(key, ct);
        if (hit is not null)
            return hit.Value;

        var generation = _cache.InvalidationGeneration;
        var value = await load(ct);

        // A save that changed this data while we were reading it has already removed the entry; storing the value we
        // read before that save would bring the old data back for the whole lifetime.
        if (_cache.InvalidationGeneration == generation)
            await _cache.SetAsync(key, new Entry<T>(value), TimeSpan.FromMinutes(_options.CurrentValue.LifetimeMinutes), ct);

        return value;
    }

    /// <summary>Wraps the value so a cached null (no settings row yet) is told apart from a miss.</summary>
    private sealed record Entry<T>(T Value);
}
