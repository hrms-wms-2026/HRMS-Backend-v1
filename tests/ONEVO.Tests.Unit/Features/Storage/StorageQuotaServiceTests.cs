using Microsoft.Extensions.Options;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.Quota.Helpers;
using ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Storage.Quota;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage;

public class StorageQuotaServiceTests
{
    private const long Gb = 1024L * 1024L * 1024L;

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string CoreHrStorageReference =
        """[{"min_employees":1,"max_employees":50,"storage_gb":25},{"min_employees":51,"max_employees":200,"storage_gb":100}]""";

    private readonly FakeTenantSubscriptionRepository _subscriptions = new();
    private readonly FakeModuleCatalogRepository _catalog = new();
    private readonly FakeTenantStorageStatsRepository _stats = new();

    private StorageQuotaService CreateService(long? defaultLimitGb = null) =>
        new(
            _subscriptions,
            _catalog,
            _stats,
            Options.Create(new StorageQuotaOptions { DefaultStorageLimitGb = defaultLimitGb }));

    private void GiveTenantActiveSubscription(
        Guid tenantId,
        string status = "active",
        string modulesJson = """["core_hr"]""",
        string companySizeRange = "51-200")
    {
        _subscriptions.Subscriptions.Add(new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Status = status,
            SelectedModulesJson = modulesJson,
            CompanySizeRange = companySizeRange,
            CreatedAt = DateTimeOffset.UtcNow
        });

        if (_catalog.Items.Count == 0)
        {
            _catalog.Items.Add(new ModuleCatalogItem
            {
                ModuleKey = "core_hr",
                IsStorageConsuming = true,
                IsActive = true,
                StorageReference = CoreHrStorageReference
            });
        }
    }

    // ---- Limit resolution precedence ----

    [Fact]
    public async Task Limit_ComesFromActiveSubscriptionModules()
    {
        GiveTenantActiveSubscription(TenantA);

        var result = await CreateService(defaultLimitGb: 5).GetTenantStorageLimitAsync(TenantA);

        Assert.True(result.IsSuccess);
        Assert.Equal(100 * Gb, result.Value!.LimitBytes);
        Assert.Equal("subscription_modules", result.Value.Source);
    }

    [Fact]
    public async Task Limit_FallsBackToPlatformDefault_WhenNoSubscription()
    {
        var result = await CreateService(defaultLimitGb: 10).GetTenantStorageLimitAsync(TenantA);

        Assert.True(result.IsSuccess);
        Assert.Equal(10 * Gb, result.Value!.LimitBytes);
        Assert.Equal("platform_default", result.Value.Source);
    }

    [Fact]
    public async Task Limit_FallsBackToPlatformDefault_WhenModulesContributeNoStorage()
    {
        GiveTenantActiveSubscription(TenantA);
        _catalog.Items[0].IsStorageConsuming = false;

        var result = await CreateService(defaultLimitGb: 10).GetTenantStorageLimitAsync(TenantA);

        Assert.True(result.IsSuccess);
        Assert.Equal("platform_default", result.Value!.Source);
    }

    /// <summary>
    /// Reproduces the local/dev "logo upload blocked by storage_not_entitled" bug: an active
    /// subscription whose selected modules exist in the catalog but every one has an empty/
    /// placeholder storage_reference (the real seeded state of module_catalog today - see
    /// PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md), with no platform default
    /// configured. This must deny, never silently grant storage.
    /// </summary>
    [Fact]
    public async Task Limit_Denied_WhenSubscriptionModulesContributeZeroAndNoDefaultConfigured()
    {
        GiveTenantActiveSubscription(TenantA);
        _catalog.Items[0].IsStorageConsuming = false;

        var result = await CreateService(defaultLimitGb: null).GetTenantStorageLimitAsync(TenantA);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.NotEntitled, result.Error);
    }

    [Fact]
    public async Task Limit_Denied_WhenNothingResolves()
    {
        var result = await CreateService(defaultLimitGb: null).GetTenantStorageLimitAsync(TenantA);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.NotEntitled, result.Error);
    }

    [Fact]
    public async Task Limit_IgnoresCancelledSubscription()
    {
        GiveTenantActiveSubscription(TenantA, status: "cancelled");

        var result = await CreateService(defaultLimitGb: null).GetTenantStorageLimitAsync(TenantA);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Limit_FailsSafely_WhenTenantContextMissing()
    {
        var result = await CreateService(defaultLimitGb: 10).GetTenantStorageLimitAsync(Guid.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.TenantContextMissing, result.Error);
    }

    [Fact]
    public async Task Limit_ZeroOrNegativePlatformDefault_MeansNoDefault()
    {
        var result = await CreateService(defaultLimitGb: 0).GetTenantStorageLimitAsync(TenantA);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    // ---- Usage reading ----

    [Fact]
    public async Task Usage_MissingRow_IsZeroUsage()
    {
        var result = await CreateService().GetTenantStorageUsageAsync(TenantA);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalUsedBytes);
        Assert.Null(result.Value.LastCalculatedAt);
    }

    [Fact]
    public async Task Usage_SumsStoredAndReservedCounters()
    {
        _stats.Rows[TenantA] = new TenantStorageStats
        {
            TenantId = TenantA,
            UsedR2Bytes = 10,
            UsedDbBytes = 20,
            ReservedR2Bytes = 30
        };

        var result = await CreateService().GetTenantStorageUsageAsync(TenantA);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value!.TotalUsedBytes);
        Assert.Equal(30, result.Value.ReservedR2Bytes);
    }

    // ---- CanConsume decisions ----

    [Fact]
    public async Task CanConsume_Allowed_UnderQuota()
    {
        GiveTenantActiveSubscription(TenantA);
        _stats.Rows[TenantA] = new TenantStorageStats { TenantId = TenantA, UsedR2Bytes = 10 * Gb };

        var result = await CreateService().CanConsumeStorageAsync(TenantA, 1 * Gb);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Allowed);
        Assert.Null(result.Value.DenialReason);
        Assert.Equal(90 * Gb, result.Value.RemainingBytes);
    }

    [Fact]
    public async Task CanConsume_Allowed_AtExactLimitBoundary()
    {
        GiveTenantActiveSubscription(TenantA);
        _stats.Rows[TenantA] = new TenantStorageStats { TenantId = TenantA, UsedR2Bytes = 99 * Gb };

        var result = await CreateService().CanConsumeStorageAsync(TenantA, 1 * Gb);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Allowed);
    }

    [Fact]
    public async Task CanConsume_Denied_OneByteOverLimit()
    {
        GiveTenantActiveSubscription(TenantA);
        _stats.Rows[TenantA] = new TenantStorageStats { TenantId = TenantA, UsedR2Bytes = 99 * Gb };

        var result = await CreateService().CanConsumeStorageAsync(TenantA, (1 * Gb) + 1);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Allowed);
        Assert.Equal(StorageQuotaErrorCodes.QuotaExceeded, result.Value.DenialReason);
    }

    [Fact]
    public async Task CanConsume_CountsReservedBytesAgainstQuota()
    {
        GiveTenantActiveSubscription(TenantA);
        _stats.Rows[TenantA] = new TenantStorageStats
        {
            TenantId = TenantA,
            UsedR2Bytes = 50 * Gb,
            ReservedR2Bytes = 50 * Gb
        };

        var result = await CreateService().CanConsumeStorageAsync(TenantA, 1);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Allowed);
    }

    [Fact]
    public async Task CanConsume_ZeroBytes_IsInvalid()
    {
        GiveTenantActiveSubscription(TenantA);

        var result = await CreateService().CanConsumeStorageAsync(TenantA, 0);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.InvalidFileSize, result.Error);
    }

    [Fact]
    public async Task CanConsume_NegativeBytes_IsInvalid()
    {
        GiveTenantActiveSubscription(TenantA);

        var result = await CreateService().CanConsumeStorageAsync(TenantA, -5);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.InvalidFileSize, result.Error);
    }

    // ---- EnsureCanConsume guard mapping ----

    [Fact]
    public async Task Ensure_Succeeds_UnderQuota()
    {
        GiveTenantActiveSubscription(TenantA);

        var result = await CreateService().EnsureCanConsumeStorageAsync(TenantA, 1 * Gb);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Ensure_Returns409WithStructuredCode_WhenQuotaExceeded()
    {
        GiveTenantActiveSubscription(TenantA);
        _stats.Rows[TenantA] = new TenantStorageStats { TenantId = TenantA, UsedR2Bytes = 100 * Gb };

        var result = await CreateService().EnsureCanConsumeStorageAsync(TenantA, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.QuotaExceeded, result.Error);
    }

    [Fact]
    public async Task Ensure_Returns403_WhenNotEntitled()
    {
        var result = await CreateService(defaultLimitGb: null).EnsureCanConsumeStorageAsync(TenantA, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.NotEntitled, result.Error);
    }

    [Fact]
    public async Task Ensure_Returns400_WhenTenantContextMissing()
    {
        var result = await CreateService(defaultLimitGb: 10).EnsureCanConsumeStorageAsync(Guid.Empty, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.TenantContextMissing, result.Error);
    }

    // ---- Tenant isolation ----

    [Fact]
    public async Task TenantIsolation_OtherTenantsUsage_DoesNotCount()
    {
        GiveTenantActiveSubscription(TenantA);
        _stats.Rows[TenantB] = new TenantStorageStats { TenantId = TenantB, UsedR2Bytes = 500 * Gb };

        var result = await CreateService().EnsureCanConsumeStorageAsync(TenantA, 1 * Gb);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantsSubscription_DoesNotGrantLimit()
    {
        // Only tenant B has a subscription; tenant A must not inherit it.
        GiveTenantActiveSubscription(TenantB);

        var result = await CreateService(defaultLimitGb: null).EnsureCanConsumeStorageAsync(TenantA, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    // ---- Fakes ----

    private sealed class FakeTenantSubscriptionRepository : ITenantSubscriptionRepository
    {
        public List<TenantSubscription> Subscriptions { get; } = [];

        public Task AddAsync(TenantSubscription subscription, CancellationToken ct = default)
        {
            Subscriptions.Add(subscription);
            return Task.CompletedTask;
        }

        public Task<TenantSubscription?> GetByGatewaySubscriptionRefAsync(string gatewayRef, CancellationToken ct = default) =>
            Task.FromResult(Subscriptions.FirstOrDefault(s => s.GatewaySubscriptionRef == gatewayRef));

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Subscriptions.FirstOrDefault(s => s.TenantId == tenantId));

        public Task<TenantSubscription?> GetLatestActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        {
            var match = Subscriptions
                .Where(s => s.TenantId == tenantId
                    && SubscriptionStatusRules.ActiveStatuses.Contains(s.Status, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
            return Task.FromResult(match);
        }
    }

    private sealed class FakeModuleCatalogRepository : IModuleCatalogRepository
    {
        public List<ModuleCatalogItem> Items { get; } = [];

        public Task<IReadOnlyList<ModuleCatalogItem>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModuleCatalogItem>>(Items);

        public Task<ModuleCatalogItem?> GetByKeyAsync(string moduleKey, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(i => i.ModuleKey == moduleKey));
    }

    private sealed class FakeTenantStorageStatsRepository : ITenantStorageStatsRepository
    {
        public Dictionary<Guid, TenantStorageStats> Rows { get; } = [];

        public Task<TenantStorageStats?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        {
            Rows.TryGetValue(tenantId, out var row);
            return Task.FromResult(row);
        }

        public Task<bool> TryReserveBytesAsync(Guid tenantId, long bytes, long limitBytes, CancellationToken ct = default)
        {
            if (bytes > limitBytes)
            {
                return Task.FromResult(false);
            }

            if (!Rows.TryGetValue(tenantId, out var row))
            {
                Rows[tenantId] = new TenantStorageStats
                {
                    TenantId = tenantId,
                    UsedR2Bytes = 0,
                    UsedDbBytes = 0,
                    ReservedR2Bytes = bytes,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                return Task.FromResult(true);
            }

            if (row.UsedR2Bytes + row.ReservedR2Bytes + bytes > limitBytes)
            {
                return Task.FromResult(false);
            }

            row.ReservedR2Bytes += bytes;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        public Task ReleaseReservedBytesAsync(Guid tenantId, long bytes, CancellationToken ct = default)
        {
            if (Rows.TryGetValue(tenantId, out var row))
            {
                row.ReservedR2Bytes = Math.Max(0, row.ReservedR2Bytes - bytes);
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task CommitReservedToUsedAsync(Guid tenantId, long bytes, CancellationToken ct = default)
        {
            if (Rows.TryGetValue(tenantId, out var row))
            {
                row.ReservedR2Bytes = Math.Max(0, row.ReservedR2Bytes - bytes);
                row.UsedR2Bytes += bytes;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }
    }
}
