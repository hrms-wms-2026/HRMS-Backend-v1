using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.SharedState;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

/// <summary>
/// Process-local cache. Removals are also published on the shared invalidation bus (task 21), so every instance
/// drops its copy; values themselves are never shared.
/// </summary>
public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ISharedInvalidationBus _bus;
    private readonly ConcurrentDictionary<string, bool> _keys = new();

    public MemoryCacheService(IMemoryCache cache, ISharedInvalidationBus? bus = null)
    {
        _cache = cache;
        _bus = bus ?? new LocalInvalidationBus();
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var opts = new MemoryCacheEntryOptions();
        if (expiry.HasValue)
            opts.SetAbsoluteExpiration(expiry.Value);
        opts.RegisterPostEvictionCallback((k, _, _, _) => _keys.TryRemove(k.ToString()!, out _));

        _cache.Set(key, value, opts);
        _keys.TryAdd(key, true);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        RemoveLocal(key);
        _bus.Publish(new Invalidation(InvalidationKinds.CacheKey, key));
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        RemoveByPrefixLocal(prefix);
        _bus.Publish(new Invalidation(InvalidationKinds.CachePrefix, prefix));
        return Task.CompletedTask;
    }

    /// <summary>This instance only; used for removals published by other instances.</summary>
    public void RemoveLocal(string key)
    {
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    /// <summary>This instance only. An empty prefix removes every entry this service stored.</summary>
    public void RemoveByPrefixLocal(string prefix)
    {
        foreach (var key in _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            RemoveLocal(key);
    }
}
