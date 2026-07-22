namespace ONEVO.Application.Features.Storage.Quota.DTOs.Responses;

/// <summary>
/// A tenant's current storage usage counters, read from tenant_storage_stats.
/// A tenant with no stats row reports zero for every counter.
/// </summary>
public sealed record TenantStorageUsageDto(
    long UsedR2Bytes,
    long UsedDbBytes,
    long ReservedR2Bytes,
    long TotalUsedBytes,
    DateTimeOffset? LastCalculatedAt);
