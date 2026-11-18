using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.Sessions;

/// <summary>Settings for the ticket-store cache (section <c>Auth:SessionTicketCache</c>).</summary>
public sealed class SessionTicketCacheOptions
{
    public const string SectionName = "Auth:SessionTicketCache";

    /// <summary>How long a built ticket is reused. 0 turns the cache off.</summary>
    public int LifetimeSeconds { get; set; } = 30;

    /// <summary>After an eviction, rebuilt tickets for the evicted session/user/tenant are not cached
    /// for this long, so a request that read the rows just before the change committed cannot put
    /// stale data back.</summary>
    public int InvalidationGraceSeconds { get; set; } = 2;

    /// <summary>RenewAsync writes ExpiresAt/LastActivityAt at most this often per session. 0 writes every time.</summary>
    public int RenewWriteIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// What the ticket stores need from the cache on top of eviction. Kept small and storage-agnostic so
/// task 21 can swap in a shared (distributed) implementation.
/// </summary>
public interface ISessionTicketStoreCache : ISessionTicketCache
{
    /// <summary>Remove one cached entry with no grace window. Only for writes the ticket store makes
    /// itself after they are committed (renewal), never for revocations.</summary>
    void DropSession(string keyHash);

    /// <summary>Timestamp to take before the first database read of a rebuild; passed back to <see cref="Set"/>.</summary>
    DateTimeOffset BeginBuild();

    bool TryGet<T>(string keyHash, out T? value) where T : class;

    /// <summary>As <see cref="TryGet{T}(string, out T)"/>, also returning when the entry's build
    /// started, so a caller can replace the value (for example a renewed expiry) with
    /// <see cref="Set"/> without making it look newer than it is.</summary>
    bool TryGet<T>(string keyHash, out T? value, out DateTimeOffset buildStartedAt) where T : class;

    /// <summary>Caches <paramref name="value"/> unless an eviction for this session, user, scope or
    /// everything happened at or after <paramref name="buildStartedAt"/> (or within the grace window).</summary>
    void Set<T>(string keyHash, T value, Guid userId, Guid scopeId, DateTimeOffset buildStartedAt) where T : class;

    /// <summary>True when RenewAsync should write to the database now; false when it wrote recently.</summary>
    bool ShouldWriteRenewal(string keyHash);

    /// <summary>Records a successful renewal write. Called only after the write commits, so a failed
    /// write never suppresses the next attempt.</summary>
    void RecordRenewalWrite(string keyHash);
}

/// <summary>
/// In-process <see cref="ISessionTicketStoreCache"/> over <see cref="IMemoryCache"/>.
/// <para>
/// Eviction leaves a marker (eviction time + grace) for the session, user, scope or globally. An
/// entry is served only if its build started after every marker that applies to it, so evicting a
/// user or tenant never needs to know which session keys belong to them, and a rebuild that raced
/// the eviction is never stored.
/// </para>
/// </summary>
public sealed class SessionTicketCache : ISessionTicketStoreCache
{
    private const string EntryPrefix = "stc:e:";
    private const string SessionMarkerPrefix = "stc:ms:";
    private const string UserMarkerPrefix = "stc:mu:";
    private const string ScopeMarkerPrefix = "stc:mt:";
    private const string RenewPrefix = "stc:r:";

    private readonly IMemoryCache _cache;
    private readonly IDateTimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _grace;
    private readonly TimeSpan _renewInterval;
    private long _globalMarkerTicks = DateTimeOffset.MinValue.UtcTicks;

    public SessionTicketCache(IMemoryCache cache, IDateTimeProvider clock, IOptions<SessionTicketCacheOptions> options)
    {
        _cache = cache;
        _clock = clock;
        var value = options.Value;
        _lifetime = TimeSpan.FromSeconds(Math.Max(0, value.LifetimeSeconds));
        _grace = TimeSpan.FromSeconds(Math.Max(0, value.InvalidationGraceSeconds));
        _renewInterval = TimeSpan.FromSeconds(Math.Max(0, value.RenewWriteIntervalSeconds));
    }

    private bool Enabled => _lifetime > TimeSpan.Zero;

    // Markers must outlive any entry they could invalidate (built at most _lifetime ago) plus the
    // grace window. Memory-cache expiry runs on wall-clock time, so a margin is added.
    private TimeSpan MarkerRetention => _lifetime + _grace + TimeSpan.FromSeconds(5);

    public DateTimeOffset BeginBuild() => _clock.UtcNow;

    public bool TryGet<T>(string keyHash, out T? value) where T : class =>
        TryGet(keyHash, out value, out _);

    public bool TryGet<T>(string keyHash, out T? value, out DateTimeOffset buildStartedAt) where T : class
    {
        value = null;
        buildStartedAt = default;
        if (!Enabled || !_cache.TryGetValue(EntryPrefix + keyHash, out Entry? entry) || entry is null)
            return false;

        if (entry.Value is not T typed || !IsServable(entry))
        {
            _cache.Remove(EntryPrefix + keyHash);
            return false;
        }

        value = typed;
        buildStartedAt = entry.BuildStartedAt;
        return true;
    }

    public void Set<T>(string keyHash, T value, Guid userId, Guid scopeId, DateTimeOffset buildStartedAt) where T : class
    {
        if (!Enabled)
            return;

        var entry = new Entry(value, keyHash, userId, scopeId, buildStartedAt);
        if (!IsServable(entry))
            return;

        _cache.Set(EntryPrefix + keyHash, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _lifetime,
            Size = 1,
        });
    }

    public bool ShouldWriteRenewal(string keyHash)
    {
        if (_renewInterval <= TimeSpan.Zero)
            return true;

        return !_cache.TryGetValue(RenewPrefix + keyHash, out DateTimeOffset lastWrite)
            || _clock.UtcNow - lastWrite >= _renewInterval;
    }

    public void RecordRenewalWrite(string keyHash)
    {
        if (_renewInterval <= TimeSpan.Zero)
            return;

        _cache.Set(RenewPrefix + keyHash, _clock.UtcNow, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _renewInterval,
            Size = 1,
        });
    }

    public void DropSession(string keyHash) => _cache.Remove(EntryPrefix + keyHash);

    public void EvictSession(string keyHash)
    {
        _cache.Remove(EntryPrefix + keyHash);
        SetMarker(SessionMarkerPrefix + keyHash);
    }

    public void EvictUser(Guid userId) => SetMarker(UserMarkerPrefix + userId.ToString("N"));

    public void EvictTenant(Guid tenantId) => SetMarker(ScopeMarkerPrefix + tenantId.ToString("N"));

    public void EvictAll()
    {
        var marker = (_clock.UtcNow + _grace).UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _globalMarkerTicks);
            if (current >= marker)
                return;
        }
        while (Interlocked.CompareExchange(ref _globalMarkerTicks, marker, current) != current);
    }

    private void SetMarker(string key)
    {
        var marker = _clock.UtcNow + _grace;
        // Keep the latest marker if two evictions race; a lost update could only shorten the
        // grace window by a few milliseconds, never make an evicted entry servable again, because
        // both values are at or after the eviction time.
        if (_cache.TryGetValue(key, out DateTimeOffset existing) && existing >= marker)
            return;

        _cache.Set(key, marker, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = MarkerRetention,
            Size = 1,
        });
    }

    private bool IsServable(Entry entry)
    {
        var now = _clock.UtcNow;
        if (now - entry.BuildStartedAt >= _lifetime || entry.BuildStartedAt > now)
            return false;

        var built = entry.BuildStartedAt;
        return built.UtcTicks > Interlocked.Read(ref _globalMarkerTicks)
            && IsAfterMarker(SessionMarkerPrefix + entry.KeyHash, built)
            && IsAfterMarker(UserMarkerPrefix + entry.UserId.ToString("N"), built)
            && IsAfterMarker(ScopeMarkerPrefix + entry.ScopeId.ToString("N"), built);
    }

    private bool IsAfterMarker(string markerKey, DateTimeOffset built) =>
        !_cache.TryGetValue(markerKey, out DateTimeOffset marker) || built > marker;

    private sealed record Entry(object Value, string KeyHash, Guid UserId, Guid ScopeId, DateTimeOffset BuildStartedAt);
}
