using ONEVO.Domain.Features.Storage.Quota.Entities;

namespace ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces;

public interface ITenantStorageStatsRepository
{
    /// <summary>
    /// Returns the cached usage counters for a tenant, or null when the tenant
    /// has never consumed storage (no row means zero usage).
    /// </summary>
    Task<TenantStorageStats?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Atomically reserves <paramref name="bytes"/> for the tenant in a single
    /// SQL statement, creating the tenant_storage_stats row if it does not yet
    /// exist. Succeeds only when used_r2_bytes + reserved_r2_bytes + bytes stays
    /// within <paramref name="limitBytes"/>. Returns false (no row changed) when
    /// the reservation would exceed the limit.
    /// </summary>
    Task<bool> TryReserveBytesAsync(Guid tenantId, long bytes, long limitBytes, CancellationToken ct = default);

    /// <summary>
    /// Atomically releases a previously reserved amount back to the pool.
    /// reserved_r2_bytes never goes below zero. Used on cancel/failure.
    /// </summary>
    Task ReleaseReservedBytesAsync(Guid tenantId, long bytes, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves bytes from reserved_r2_bytes to used_r2_bytes on
    /// successful upload completion.
    /// </summary>
    Task CommitReservedToUsedAsync(Guid tenantId, long bytes, CancellationToken ct = default);
}
