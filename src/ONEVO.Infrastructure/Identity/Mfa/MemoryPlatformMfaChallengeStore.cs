using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.Mfa;

/// <summary>
/// Process-local admin MFA challenge store. Challenge state is lost on API restart and is not
/// shared across instances — acceptable for a short-lived (~10 minute) login challenge in the
/// current single-instance deployment, but should be replaced with a PostgreSQL-backed store
/// (mirroring <see cref="PostgresMfaChallengeStore"/>, which the tenant side now uses
/// exclusively) before running multiple API instances behind a load balancer.
/// </summary>
public sealed class MemoryPlatformMfaChallengeStore : IPlatformMfaChallengeStore
{
    private const string CacheKeyPrefix = "auth:admin-mfa-challenge:";

    private readonly IMemoryCache _cache;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly object _sync = new();

    public MemoryPlatformMfaChallengeStore(
        IMemoryCache cache,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock)
    {
        _cache = cache;
        _tokens = tokens;
        _clock = clock;
    }

    public Task<string> CreateAsync(
        Guid platformUserId,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        return CreateCoreAsync(platformUserId, pendingGoogleSub: null, lifetime, ct);
    }

    public Task<string> CreateForGoogleLinkAsync(
        Guid platformUserId,
        string googleSub,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSub);
        return CreateCoreAsync(platformUserId, googleSub, lifetime, ct);
    }

    private Task<string> CreateCoreAsync(
        Guid platformUserId,
        string? pendingGoogleSub,
        TimeSpan lifetime,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var rawChallenge = _tokens.GenerateOpaqueToken();
        var expiresAt = _clock.UtcNow.Add(lifetime);
        var state = new PlatformMfaChallengeState(platformUserId, expiresAt, FailedAttempts: 0, pendingGoogleSub);

        SetUntil(GetCacheKey(rawChallenge), state);
        return Task.FromResult(rawChallenge);
    }

    public Task<PlatformMfaChallengeState?> GetAsync(
        string challenge,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_cache.TryGetValue(GetCacheKey(challenge), out PlatformMfaChallengeState? state)
                || state is null)
            {
                return Task.FromResult<PlatformMfaChallengeState?>(null);
            }

            if (state.ExpiresAt <= _clock.UtcNow)
            {
                _cache.Remove(GetCacheKey(challenge));
                return Task.FromResult<PlatformMfaChallengeState?>(null);
            }

            return Task.FromResult<PlatformMfaChallengeState?>(state);
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
            if (!_cache.TryGetValue(cacheKey, out PlatformMfaChallengeState? state)
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
            SetUntil(cacheKey, updated);
            return Task.FromResult(true);
        }
    }

    public Task<PlatformMfaChallengeState?> TryConsumeAsync(
        string challenge,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var cacheKey = GetCacheKey(challenge);
            if (!_cache.TryGetValue(cacheKey, out PlatformMfaChallengeState? state)
                || state is null
                || state.ExpiresAt <= _clock.UtcNow)
            {
                _cache.Remove(cacheKey);
                return Task.FromResult<PlatformMfaChallengeState?>(null);
            }

            _cache.Remove(cacheKey);
            return Task.FromResult<PlatformMfaChallengeState?>(state);
        }
    }

    /// <summary>
    /// Stores <paramref name="state"/> until its <see cref="PlatformMfaChallengeState.ExpiresAt"/>.
    /// The TTL is relative to the injected clock rather than an absolute expiry, because
    /// <see cref="IMemoryCache"/> evaluates absolute expirations against its own system clock;
    /// mixing the two would evict entries early (or late) whenever they disagree.
    /// </summary>
    private void SetUntil(string cacheKey, PlatformMfaChallengeState state)
    {
        var remaining = state.ExpiresAt - _clock.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _cache.Remove(cacheKey);
            return;
        }

        _cache.Set(cacheKey, state, remaining);
    }

    private string GetCacheKey(string challenge)
    {
        return CacheKeyPrefix + _tokens.HashToken(challenge);
    }
}
