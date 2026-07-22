using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.Quota.DTOs.Responses;
using ONEVO.Application.Features.Storage.Quota.Helpers;
using ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Storage.Quota;

/// <summary>
/// Phase 1 storage quota enforcement. Deliberately written as simple,
/// step-by-step logic so every decision point is auditable. See
/// IStorageQuotaService for the limit precedence and usage-source contract.
/// </summary>
public sealed class StorageQuotaService : IStorageQuotaService
{
    private const long BytesPerGigabyte = 1024L * 1024L * 1024L;

    private const string SourceSubscriptionModules = "subscription_modules";
    private const string SourcePlatformDefault = "platform_default";

    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly IModuleCatalogRepository _moduleCatalog;
    private readonly ITenantStorageStatsRepository _storageStats;
    private readonly StorageQuotaOptions _options;

    public StorageQuotaService(
        ITenantSubscriptionRepository subscriptions,
        IModuleCatalogRepository moduleCatalog,
        ITenantStorageStatsRepository storageStats,
        IOptions<StorageQuotaOptions> options)
    {
        _subscriptions = subscriptions;
        _moduleCatalog = moduleCatalog;
        _storageStats = storageStats;
        _options = options.Value;
    }

    public async Task<Result<TenantStorageLimitDto>> GetTenantStorageLimitAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Step 1: refuse to resolve anything without a trusted tenant id.
        if (tenantId == Guid.Empty)
            return Result<TenantStorageLimitDto>.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        // Step 2: find the tenant's active subscription.
        var subscription = await _subscriptions.GetLatestActiveByTenantIdAsync(tenantId, ct);

        // Step 3: if there is an active subscription, sum the storage its
        // selected modules contribute for the subscription's company size.
        if (subscription is not null)
        {
            var selectedModuleKeys = ParseSelectedModuleKeys(subscription.SelectedModulesJson);

            if (selectedModuleKeys.Count > 0)
            {
                var catalog = await _moduleCatalog.GetAllAsync(ct);

                var subscriptionBytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(
                    catalog,
                    selectedModuleKeys,
                    subscription.CompanySizeRange);

                if (subscriptionBytes > 0)
                    return Result<TenantStorageLimitDto>.Success(
                        new TenantStorageLimitDto(subscriptionBytes, SourceSubscriptionModules));
            }
        }

        // Step 4: fall back to the configured platform default, if any.
        var defaultGb = _options.DefaultStorageLimitGb;
        if (defaultGb.HasValue && defaultGb.Value > 0)
            return Result<TenantStorageLimitDto>.Success(
                new TenantStorageLimitDto(defaultGb.Value * BytesPerGigabyte, SourcePlatformDefault));

        // Step 5: no resolvable allowance. Fail safe: the tenant is not
        // entitled to consume storage. Null/"unlimited" limits do not exist in
        // Phase 1; an unresolvable limit is always a denial, never unlimited.
        return Result<TenantStorageLimitDto>.Forbidden(StorageQuotaErrorCodes.NotEntitled);
    }

    public async Task<Result<TenantStorageUsageDto>> GetTenantStorageUsageAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Step 1: refuse without a trusted tenant id.
        if (tenantId == Guid.Empty)
            return Result<TenantStorageUsageDto>.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        // Step 2: read the cached counters. A missing row means the tenant has
        // never consumed storage, which is zero usage by definition.
        var stats = await _storageStats.GetByTenantIdAsync(tenantId, ct);

        if (stats is null)
            return Result<TenantStorageUsageDto>.Success(
                new TenantStorageUsageDto(0, 0, 0, 0, null));

        // Step 3: total usage counts stored bytes plus active reservations so
        // concurrent in-flight uploads cannot oversubscribe the pool.
        var totalUsed = stats.UsedR2Bytes + stats.UsedDbBytes + stats.ReservedR2Bytes;

        return Result<TenantStorageUsageDto>.Success(
            new TenantStorageUsageDto(
                stats.UsedR2Bytes,
                stats.UsedDbBytes,
                stats.ReservedR2Bytes,
                totalUsed,
                stats.LastCalculatedAt));
    }

    public async Task<Result<StorageQuotaCheckDto>> CanConsumeStorageAsync(
        Guid tenantId,
        long bytesToAdd,
        CancellationToken ct = default)
    {
        // Step 1: validate inputs before touching any data.
        if (tenantId == Guid.Empty)
            return Result<StorageQuotaCheckDto>.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        if (bytesToAdd <= 0)
            return Result<StorageQuotaCheckDto>.Failure(StorageQuotaErrorCodes.InvalidFileSize);

        // Step 2: resolve the allowance. An unresolvable allowance propagates
        // as-is (403 storage_not_entitled).
        var limitResult = await GetTenantStorageLimitAsync(tenantId, ct);
        if (!limitResult.IsSuccess || limitResult.Value is null)
            return Result<StorageQuotaCheckDto>.Failure(
                limitResult.Error ?? StorageQuotaErrorCodes.NotEntitled,
                limitResult.StatusCode ?? 403);

        // Step 3: read current usage.
        var usageResult = await GetTenantStorageUsageAsync(tenantId, ct);
        if (!usageResult.IsSuccess || usageResult.Value is null)
            return Result<StorageQuotaCheckDto>.Failure(
                usageResult.Error ?? StorageQuotaErrorCodes.TenantContextMissing,
                usageResult.StatusCode ?? 400);

        var limitBytes = limitResult.Value.LimitBytes;
        var totalUsedBytes = usageResult.Value.TotalUsedBytes;

        // Step 4: decide. Landing exactly on the limit is allowed; crossing it
        // is not.
        var wouldUse = totalUsedBytes + bytesToAdd;
        var allowed = wouldUse <= limitBytes;

        var remaining = limitBytes - totalUsedBytes;
        if (remaining < 0)
            remaining = 0;

        var decision = new StorageQuotaCheckDto(
            Allowed: allowed,
            LimitBytes: limitBytes,
            TotalUsedBytes: totalUsedBytes,
            RequestedBytes: bytesToAdd,
            RemainingBytes: remaining,
            DenialReason: allowed ? null : StorageQuotaErrorCodes.QuotaExceeded);

        return Result<StorageQuotaCheckDto>.Success(decision);
    }

    public async Task<Result> EnsureCanConsumeStorageAsync(
        Guid tenantId,
        long bytesToAdd,
        CancellationToken ct = default)
    {
        // Step 1: run the full check.
        var checkResult = await CanConsumeStorageAsync(tenantId, bytesToAdd, ct);

        // Step 2: invalid input or missing entitlement fails with the original
        // status code (400 or 403).
        if (!checkResult.IsSuccess || checkResult.Value is null)
            return Result.Failure(
                checkResult.Error ?? StorageQuotaErrorCodes.NotEntitled,
                checkResult.StatusCode ?? 400);

        // Step 3: an over-quota decision maps to the project's conflict
        // convention (409) with the structured storage_quota_exceeded code.
        if (!checkResult.Value.Allowed)
            return Result.Conflict(StorageQuotaErrorCodes.QuotaExceeded);

        return Result.Success();
    }

    public async Task<Result> ReserveStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        // Step 1: validate inputs before touching any data.
        if (tenantId == Guid.Empty)
            return Result.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        if (bytes <= 0)
            return Result.Failure(StorageQuotaErrorCodes.InvalidFileSize);

        // Step 2: resolve the allowance. An unresolvable allowance propagates
        // as-is (403 storage_not_entitled).
        var limitResult = await GetTenantStorageLimitAsync(tenantId, ct);
        if (!limitResult.IsSuccess || limitResult.Value is null)
            return Result.Failure(
                limitResult.Error ?? StorageQuotaErrorCodes.NotEntitled,
                limitResult.StatusCode ?? 403);

        // Step 3: atomically reserve. The repository performs the check and the
        // increment in one SQL statement, so concurrent reservations cannot
        // oversubscribe the limit.
        var reserved = await _storageStats.TryReserveBytesAsync(tenantId, bytes, limitResult.Value.LimitBytes, ct);

        if (!reserved)
            return Result.Conflict(StorageQuotaErrorCodes.QuotaExceeded);

        return Result.Success();
    }

    public async Task<Result> ReleaseReservedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        if (bytes <= 0)
            return Result.Success();

        await _storageStats.ReleaseReservedBytesAsync(tenantId, bytes, ct);
        return Result.Success();
    }

    public async Task<Result> CommitReservedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        if (bytes <= 0)
            return Result.Success();

        await _storageStats.CommitReservedToUsedAsync(tenantId, bytes, ct);
        return Result.Success();
    }

    private static IReadOnlyList<string> ParseSelectedModuleKeys(string selectedModulesJson)
    {
        if (string.IsNullOrWhiteSpace(selectedModulesJson))
            return [];

        List<string>? keys;
        try
        {
            keys = JsonSerializer.Deserialize<List<string>>(selectedModulesJson);
        }
        catch (JsonException)
        {
            return [];
        }

        if (keys is null)
            return [];

        var cleaned = new List<string>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            cleaned.Add(key.Trim());
        }

        return cleaned;
    }
}
