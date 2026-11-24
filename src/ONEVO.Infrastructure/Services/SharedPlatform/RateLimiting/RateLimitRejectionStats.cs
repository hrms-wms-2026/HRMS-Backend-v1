using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>
/// In-memory rolling 429 counts (Improvements task 49): 60 one-minute buckets, each holding counts
/// per tenant and subject kind. A bucket is reused when its minute comes round again, so nothing
/// older than 60 minutes is kept.
/// <para>
/// At most <see cref="MaxTenants"/> tenant keys are held. When a new tenant would go over, the
/// tenant with the oldest rejection is dropped from every bucket, so a flood across many tenants
/// cannot grow memory without limit. Requests with no tenant share one key.
/// </para>
/// <para>
/// Only 429 responses come here, never successful requests, and each one is a few dictionary
/// operations under one lock.
/// </para>
/// </summary>
public sealed class RateLimitRejectionStats : IRateLimitRejectionStats
{
    public const int DefaultMaxTenants = 5_000;

    private const int BucketCount = IRateLimitRejectionStats.MaxWindowMinutes;

    // Dictionary keys cannot be null, so "no tenant" is stored as Guid.Empty, which no tenant uses.
    private static readonly Guid NoTenant = Guid.Empty;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Bucket[] _buckets;
    private readonly LinkedList<Guid> _tenantsByLastRejection = new();
    private readonly Dictionary<Guid, LinkedListNode<Guid>> _tenantNodes = new();
    private readonly DateTimeOffset _countingSince;

    public RateLimitRejectionStats(TimeProvider time, int maxTenants = DefaultMaxTenants)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTenants, 1);
        _time = time;
        MaxTenants = maxTenants;
        _countingSince = time.GetUtcNow();
        _buckets = Enumerable.Range(0, BucketCount).Select(_ => new Bucket()).ToArray();
    }

    public int MaxTenants { get; }

    /// <summary>Tenant keys held right now, the no-tenant key included.</summary>
    public int TrackedTenantCount
    {
        get
        {
            lock (_gate)
                return _tenantNodes.Count;
        }
    }

    public void Record(Guid? tenantId, string kind)
    {
        var tenant = tenantId ?? NoTenant;
        var minute = MinuteOf(_time.GetUtcNow());

        lock (_gate)
        {
            Touch(tenant);

            var bucket = _buckets[minute % BucketCount];
            if (bucket.Minute != minute)
            {
                bucket.Minute = minute;
                bucket.Counts.Clear();
            }

            if (!bucket.Counts.TryGetValue(tenant, out var kinds))
            {
                kinds = new Dictionary<string, long>(StringComparer.Ordinal);
                bucket.Counts[tenant] = kinds;
            }

            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        }
    }

    public RateLimitRejectionSnapshot Read(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, IRateLimitRejectionStats.MaxWindowMinutes);
        var now = _time.GetUtcNow();
        var currentMinute = MinuteOf(now);
        var oldestMinute = currentMinute - minutes + 1;
        var totals = new Dictionary<(Guid Tenant, string Kind), long>();

        lock (_gate)
        {
            foreach (var bucket in _buckets)
            {
                if (bucket.Minute < oldestMinute || bucket.Minute > currentMinute)
                    continue;

                foreach (var (tenant, kinds) in bucket.Counts)
                {
                    foreach (var (kind, count) in kinds)
                        totals[(tenant, kind)] = totals.GetValueOrDefault((tenant, kind)) + count;
                }
            }
        }

        var counts = totals
            .Select(entry => new RateLimitRejectionCount(
                entry.Key.Tenant == NoTenant ? null : entry.Key.Tenant, entry.Key.Kind, entry.Value))
            .ToList();
        return new RateLimitRejectionSnapshot(now, _countingSince, counts);
    }

    private static long MinuteOf(DateTimeOffset time) => time.ToUnixTimeSeconds() / 60;

    /// <summary>Marks the tenant as the most recent; drops the oldest tenant when over the cap.</summary>
    private void Touch(Guid tenant)
    {
        if (_tenantNodes.TryGetValue(tenant, out var node))
        {
            _tenantsByLastRejection.Remove(node);
            _tenantsByLastRejection.AddLast(node);
            return;
        }

        if (_tenantNodes.Count >= MaxTenants)
        {
            var oldest = _tenantsByLastRejection.First!;
            _tenantsByLastRejection.RemoveFirst();
            _tenantNodes.Remove(oldest.Value);
            foreach (var bucket in _buckets)
                bucket.Counts.Remove(oldest.Value);
        }

        _tenantNodes[tenant] = _tenantsByLastRejection.AddLast(tenant);
    }

    private sealed class Bucket
    {
        public long Minute { get; set; } = -1;

        public Dictionary<Guid, Dictionary<string, long>> Counts { get; } = new();
    }
}
