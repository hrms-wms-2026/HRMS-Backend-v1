using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>
/// Fixed-window attempt counters for the sign-in and MFA limits (task 21). <paramref name="key"/> must already be a
/// hash of the identity (email, token, IP) - never the raw value.
/// </summary>
public interface IAttemptCounter
{
    /// <summary>Adds one attempt and returns the count in the current window, which starts at the first attempt.</summary>
    Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct);
}

/// <summary>Per-process counters: the single-instance behaviour, and the fallback when Redis fails.</summary>
public sealed class MemoryAttemptCounter(TimeProvider? time = null) : IAttemptCounter
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private long _calls;

    private sealed class Window(DateTimeOffset endsAt)
    {
        public DateTimeOffset EndsAt { get; } = endsAt;
        public long Count;
    }

    public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var current = _windows.AddOrUpdate(key, _ => new Window(now + window),
            (_, existing) => existing.EndsAt <= now ? new Window(now + window) : existing);
        var count = Interlocked.Increment(ref current.Count);
        if (Interlocked.Increment(ref _calls) % 1000 == 0)
            Sweep(now);
        return Task.FromResult(count);
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, window) in _windows)
        {
            if (window.EndsAt <= now)
                _windows.TryRemove(new KeyValuePair<string, Window>(key, window));
        }
    }
}

/// <summary>
/// Shared counters: one INCR and, on the first attempt, the window's expiry, in one atomic script. If Redis fails the
/// attempt is counted locally instead, so a Redis outage weakens the limit to per-instance but never removes it.
/// </summary>
public sealed class RedisAttemptCounter(
    IConnectionMultiplexer redis, MemoryAttemptCounter fallback, ILogger<RedisAttemptCounter> logger) : IAttemptCounter
{
    private const string Script = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    public async Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct)
    {
        try
        {
            var result = await redis.GetDatabase().ScriptEvaluateAsync(Script,
                [new RedisKey($"{SharedStateRegistration.KeyPrefix}attempts:{key}")],
                [(long)window.TotalMilliseconds]);
            return (long)result;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis attempt counter failed; counting this attempt on this instance only.");
            return await fallback.IncrementAsync(key, window, ct);
        }
    }
}
