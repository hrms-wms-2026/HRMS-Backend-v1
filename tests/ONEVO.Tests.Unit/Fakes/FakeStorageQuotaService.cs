using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.Quota.DTOs.Responses;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeStorageQuotaService : IStorageQuotaService
{
    public bool ReserveShouldSucceed { get; set; } = true;
    public string ReserveFailureError { get; set; } = "storage_quota_exceeded";
    public int ReserveFailureStatusCode { get; set; } = 409;
    public int ReserveCallCount { get; private set; }
    public int ReleaseCallCount { get; private set; }
    public int CommitCallCount { get; private set; }
    public long LastReservedBytes { get; private set; }
    public long LastReleasedBytes { get; private set; }
    public long LastCommittedBytes { get; private set; }

    public Task<Result<TenantStorageLimitDto>> GetTenantStorageLimitAsync(Guid tenantId, CancellationToken ct = default)
    {
        return Task.FromResult(Result<TenantStorageLimitDto>.Success(new TenantStorageLimitDto(1_000_000_000, "test")));
    }

    public Task<Result<TenantStorageUsageDto>> GetTenantStorageUsageAsync(Guid tenantId, CancellationToken ct = default)
    {
        return Task.FromResult(Result<TenantStorageUsageDto>.Success(new TenantStorageUsageDto(0, 0, 0, 0, null)));
    }

    public Task<Result<StorageQuotaCheckDto>> CanConsumeStorageAsync(Guid tenantId, long bytesToAdd, CancellationToken ct = default)
    {
        var decision = new StorageQuotaCheckDto(true, 1_000_000_000, 0, bytesToAdd, 1_000_000_000 - bytesToAdd, null);
        return Task.FromResult(Result<StorageQuotaCheckDto>.Success(decision));
    }

    public Task<Result> EnsureCanConsumeStorageAsync(Guid tenantId, long bytesToAdd, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ReserveStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        ReserveCallCount++;
        LastReservedBytes = bytes;
        return Task.FromResult(ReserveShouldSucceed
            ? Result.Success()
            : Result.Failure(ReserveFailureError, ReserveFailureStatusCode));
    }

    public Task<Result> ReleaseReservedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        ReleaseCallCount++;
        LastReleasedBytes = bytes;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> CommitReservedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        CommitCallCount++;
        LastCommittedBytes = bytes;
        return Task.FromResult(Result.Success());
    }
}
