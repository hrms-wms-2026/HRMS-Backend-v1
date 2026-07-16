using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity;

/// <summary>
/// Phase 1 local/single-instance implementation only. Challenge state is process-local and is lost
/// when the API restarts; another API instance cannot read it. Before production multi-instance
/// deployment, replace this implementation with approved shared cache/session-backed challenge
/// storage. Phase 1 does not define an mfa_challenges table, so this implementation must not be made
/// durable by adding an unapproved table. The browser receives only the opaque challenge in the
/// HttpOnly onevo_mfa cookie.
/// </summary>
public sealed class MemoryMfaChallengeStore : IMfaChallengeStore
{
    private const string CacheKeyPrefix = "auth:mfa-challenge:";

    private readonly IMemoryCache _cache;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly object _sync = new();

    public MemoryMfaChallengeStore(
        IMemoryCache cache,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock)
    {
        _cache = cache;
        _tokens = tokens;
        _clock = clock;
    }

    public Task<string> CreateAsync(
        Guid userId,
        Guid tenantId,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var rawChallenge = _tokens.GenerateOpaqueToken();
        var expiresAt = _clock.UtcNow.Add(lifetime);
        var state = new MfaChallengeState(userId, tenantId, expiresAt, FailedAttempts: 0);

        _cache.Set(GetCacheKey(rawChallenge), state, expiresAt);
        return Task.FromResult(rawChallenge);
    }

    public Task<MfaChallengeState?> GetAsync(
        string challenge,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_cache.TryGetValue(GetCacheKey(challenge), out MfaChallengeState? state)
                || state is null)
            {
                return Task.FromResult<MfaChallengeState?>(null);
            }

            if (state.ExpiresAt <= _clock.UtcNow)
            {
                _cache.Remove(GetCacheKey(challenge));
                return Task.FromResult<MfaChallengeState?>(null);
            }

            return Task.FromResult<MfaChallengeState?>(state);
        }
    }

    public Task<bool> RegisterFailedAttemptAsync(
        string challenge,
        int maximumAttempts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var cacheKey = GetCacheKey(challenge);
            if (!_cache.TryGetValue(cacheKey, out MfaChallengeState? state)
                || state is null
                || state.ExpiresAt <= _clock.UtcNow)
            {
                _cache.Remove(cacheKey);
                return Task.FromResult(false);
            }

            var failedAttempts = state.FailedAttempts + 1;
            if (failedAttempts >= maximumAttempts)
            {
                _cache.Remove(cacheKey);
                return Task.FromResult(false);
            }

            var updated = state with { FailedAttempts = failedAttempts };
            _cache.Set(cacheKey, updated, state.ExpiresAt);
            return Task.FromResult(true);
        }
    }

    public Task<MfaChallengeState?> TryConsumeAsync(
        string challenge,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var cacheKey = GetCacheKey(challenge);
            if (!_cache.TryGetValue(cacheKey, out MfaChallengeState? state)
                || state is null
                || state.ExpiresAt <= _clock.UtcNow)
            {
                _cache.Remove(cacheKey);
                return Task.FromResult<MfaChallengeState?>(null);
            }

            _cache.Remove(cacheKey);
            return Task.FromResult<MfaChallengeState?>(state);
        }
    }

    private string GetCacheKey(string challenge)
    {
        return CacheKeyPrefix + _tokens.HashToken(challenge);
    }
}
