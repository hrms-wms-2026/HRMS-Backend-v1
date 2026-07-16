using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, bool> _keys = new();

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

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
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        foreach (var key in _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }
}
