using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Storage.Quota.Entities;

/// <summary>
/// Cached per-tenant storage usage counters used for fast quota checks
/// (Phase 1 table <c>tenant_storage_stats</c>). Purchased limits are resolved
/// from the tenant's active subscription; this row only tracks usage.
/// </summary>
public class TenantStorageStats : ITenantOwnedEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Bytes used by active R2-backed files.</summary>
    public long UsedR2Bytes { get; set; }

    /// <summary>Bytes used by DB-backed file/content storage, if any.</summary>
    public long UsedDbBytes { get; set; }

    /// <summary>Sum of active upload reservations.</summary>
    public long ReservedR2Bytes { get; set; }

    /// <summary>Last full recalculation; null until the first background sync runs.</summary>
    public DateTimeOffset? LastCalculatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
