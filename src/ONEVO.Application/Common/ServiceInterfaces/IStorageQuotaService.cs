using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.Quota.DTOs.Responses;

namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Resolves tenant storage allowances and enforces them before any
/// storage-consuming write. Every Phase 1 handler that persists file bytes or
/// file metadata (uploads, generated documents, imports, exports, profile
/// images, verification evidence) must call
/// <see cref="EnsureCanConsumeStorageAsync"/> with the trusted server-side byte
/// count BEFORE writing to object storage or file tables.
///
/// Limit precedence (Phase 1):
///   1. Storage contributed by the modules on the tenant's active subscription
///      (module_catalog.storage_reference resolved by company size range).
///   2. Configured platform default (StorageQuota:DefaultStorageLimitGb) when
///      the subscription contributes no storage.
///   3. No resolvable allowance: deny (fail safe, 403 storage_not_entitled).
///
/// Usage source: tenant_storage_stats counters
/// (used_r2_bytes + used_db_bytes + reserved_r2_bytes). A missing row means
/// zero usage.
/// </summary>
public interface IStorageQuotaService
{
    /// <summary>Resolves the tenant's total allowed storage in bytes.</summary>
    Task<Result<TenantStorageLimitDto>> GetTenantStorageLimitAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Reads the tenant's current usage counters. Missing row = zero usage.</summary>
    Task<Result<TenantStorageUsageDto>> GetTenantStorageUsageAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the tenant may add <paramref name="bytesToAdd"/> bytes.
    /// Returns a successful Result carrying the decision; only invalid input
    /// (empty tenant, non-positive bytes) or an unresolvable entitlement is a
    /// failed Result.
    /// </summary>
    Task<Result<StorageQuotaCheckDto>> CanConsumeStorageAsync(Guid tenantId, long bytesToAdd, CancellationToken ct = default);

    /// <summary>
    /// Guard form for write paths: succeeds only when the bytes fit. Failure
    /// codes follow project conventions: 400 for invalid input or missing
    /// tenant context, 403 (storage_not_entitled) when no allowance resolves,
    /// 409 (storage_quota_exceeded) when the quota would be exceeded.
    /// </summary>
    Task<Result> EnsureCanConsumeStorageAsync(Guid tenantId, long bytesToAdd, CancellationToken ct = default);

    /// <summary>
    /// Atomically reserves <paramref name="bytes"/> against the tenant's quota
    /// in a single SQL statement (checks the resolved limit and increments
    /// tenant_storage_stats.reserved_r2_bytes together). Failure codes follow
    /// project conventions: 400 for invalid input, 403 (storage_not_entitled)
    /// when no allowance resolves, 409 (storage_quota_exceeded) when the
    /// reservation would exceed the limit.
    /// </summary>
    Task<Result> ReserveStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default);

    /// <summary>
    /// Releases a previously reserved amount back to the pool. Used when a
    /// reservation is cancelled or an upload fails after reserving. Always
    /// succeeds (idempotent — reserved_r2_bytes never goes below zero).
    /// </summary>
    Task<Result> ReleaseReservedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default);

    /// <summary>
    /// Moves <paramref name="bytes"/> from reserved_r2_bytes to used_r2_bytes on
    /// successful upload completion.
    /// </summary>
    Task<Result> CommitReservedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default);
}
