using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Caching;

/// <summary>Improvements task 22: the tenant-keyed reference-data cache.</summary>
public sealed class TenantCacheTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private sealed class Options(bool enabled) : IOptionsMonitor<ReferenceDataCacheOptions>
    {
        public ReferenceDataCacheOptions CurrentValue { get; } = new() { Enabled = enabled, LifetimeMinutes = 5 };
        public ReferenceDataCacheOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ReferenceDataCacheOptions, string?> listener) => null;
    }

    private static MemoryCacheService NewStore() => new(new MemoryCache(new MemoryCacheOptions()));

    private static TenantContextAccessor TenantContext(Guid tenantId)
    {
        var context = new TenantContextAccessor();
        context.Resolve(new TenantRegistryEntry(tenantId, "t", TenantStatus.Active, PlanCode: null));
        return context;
    }

    private static TenantCache Cache(MemoryCacheService store, TenantContextAccessor context, bool enabled = true) =>
        new(store, context, new Options(enabled));

    private sealed class Loader(string value)
    {
        public int Calls { get; private set; }

        public Task<string> Load(CancellationToken _)
        {
            Calls++;
            return Task.FromResult(value);
        }
    }

    [Fact]
    public async Task Second_read_for_the_same_tenant_is_served_from_the_cache()
    {
        var store = NewStore();
        var loader = new Loader("a");

        (await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", loader.Load)).Should().Be("a");
        (await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", loader.Load)).Should().Be("a");

        loader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Two_tenants_never_share_an_entry()
    {
        var store = NewStore();
        var forA = new Loader("tenant-a rows");
        var forB = new Loader("tenant-b rows");

        await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", forA.Load);
        var readByB = await Cache(store, TenantContext(TenantB)).GetOrCreateAsync(TenantB, "x", "", forB.Load);

        readByB.Should().Be("tenant-b rows");
        forB.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Switched_off_every_read_runs_the_query()
    {
        var store = NewStore();
        var loader = new Loader("a");

        await Cache(store, TenantContext(TenantA), enabled: false).GetOrCreateAsync(TenantA, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantA), enabled: false).GetOrCreateAsync(TenantA, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantA), enabled: false).GetOrCreateGlobalAsync("g", loader.Load);
        await Cache(store, TenantContext(TenantA), enabled: false).GetOrCreateGlobalAsync("g", loader.Load);

        loader.Calls.Should().Be(4);
    }

    [Fact]
    public async Task A_request_resolved_to_another_tenant_neither_reads_nor_fills_the_entry()
    {
        // RLS would return no rows for tenant B here; caching that empty result would hide B's data for minutes.
        var store = NewStore();
        var wrongContext = new Loader("empty");
        var ownContext = new Loader("tenant-b rows");

        await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantB, "x", "", wrongContext.Load);
        await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantB, "x", "", wrongContext.Load);
        var own = await Cache(store, TenantContext(TenantB)).GetOrCreateAsync(TenantB, "x", "", ownContext.Load);

        wrongContext.Calls.Should().Be(2);
        own.Should().Be("tenant-b rows");
        ownContext.Calls.Should().Be(1);
    }

    [Fact]
    public async Task System_mode_reads_no_tenant_rows_so_it_bypasses_the_cache()
    {
        var store = NewStore();
        var loader = new Loader("empty");

        await Cache(store, new TenantContextAccessor()).GetOrCreateAsync(TenantA, "x", "", loader.Load);
        await Cache(store, new TenantContextAccessor()).GetOrCreateAsync(TenantA, "x", "", loader.Load);

        loader.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Admin_mode_reads_every_tenant_so_it_uses_the_cache()
    {
        var store = NewStore();
        var admin = new TenantContextAccessor();
        admin.SetAdminMode();
        var loader = new Loader("a");

        await Cache(store, admin).GetOrCreateAsync(TenantA, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", loader.Load);

        loader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_missing_row_is_cached_as_null()
    {
        var store = NewStore();
        var calls = 0;
        Task<string?> Load(CancellationToken _)
        {
            calls++;
            return Task.FromResult<string?>(null);
        }

        (await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", Load)).Should().BeNull();
        (await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", Load)).Should().BeNull();

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Invalidate_drops_every_variant_for_that_tenant_only()
    {
        var store = NewStore();
        var loader = new Loader("a");
        var cacheA = Cache(store, TenantContext(TenantA));
        var cacheB = Cache(store, TenantContext(TenantB));
        await cacheA.GetOrCreateAsync(TenantA, "x", "v1", loader.Load);
        await cacheA.GetOrCreateAsync(TenantA, "x", "v2", loader.Load);
        await cacheB.GetOrCreateAsync(TenantB, "x", "v1", loader.Load);

        await cacheA.InvalidateAsync(TenantA, "x");
        await cacheA.GetOrCreateAsync(TenantA, "x", "v1", loader.Load);
        await cacheA.GetOrCreateAsync(TenantA, "x", "v2", loader.Load);
        await cacheB.GetOrCreateAsync(TenantB, "x", "v1", loader.Load);

        loader.Calls.Should().Be(5, "both tenant A variants are read again, tenant B's entry stays");
    }

    [Fact]
    public async Task InvalidateEverywhere_drops_the_name_for_every_tenant_and_the_global_entry()
    {
        var store = NewStore();
        var loader = new Loader("a");
        await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantB)).GetOrCreateAsync(TenantB, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantB)).GetOrCreateGlobalAsync("x", loader.Load);

        await Cache(store, TenantContext(TenantA)).InvalidateEverywhereAsync("x");
        await Cache(store, TenantContext(TenantA)).GetOrCreateAsync(TenantA, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantB)).GetOrCreateAsync(TenantB, "x", "", loader.Load);
        await Cache(store, TenantContext(TenantB)).GetOrCreateGlobalAsync("x", loader.Load);

        loader.Calls.Should().Be(6);
    }

    [Fact]
    public async Task A_value_read_while_the_data_was_invalidated_is_not_stored()
    {
        var store = NewStore();
        var cache = Cache(store, TenantContext(TenantA));
        var calls = 0;
        async Task<string> LoadWhileSomeoneSaves(CancellationToken ct)
        {
            calls++;
            if (calls == 1)
                await cache.InvalidateAsync(TenantA, "x", ct); // a save landed after our SELECT
            return calls == 1 ? "old" : "new";
        }

        (await cache.GetOrCreateAsync(TenantA, "x", "", LoadWhileSomeoneSaves)).Should().Be("old");
        (await cache.GetOrCreateAsync(TenantA, "x", "", LoadWhileSomeoneSaves)).Should().Be("new");
        calls.Should().Be(2);
    }

    [Fact]
    public void Keys_always_carry_the_tenant_id()
    {
        var key = ReferenceDataCacheKeys.ForTenant(TenantA, ReferenceDataCacheKeys.LeaveTypes, "active");

        key.Should().Be($"ref:leave-types:{TenantA:N}:active");
        key.Should().StartWith(ReferenceDataCacheKeys.TenantPrefix(TenantA, ReferenceDataCacheKeys.LeaveTypes));
        key.Should().StartWith(ReferenceDataCacheKeys.NamePrefix(ReferenceDataCacheKeys.LeaveTypes));
        ReferenceDataCacheKeys.Global(ReferenceDataCacheKeys.UniversalPermissions)
            .Should().StartWith(ReferenceDataCacheKeys.NamePrefix(ReferenceDataCacheKeys.UniversalPermissions));
        FluentActions.Invoking(() => ReferenceDataCacheKeys.ForTenant(Guid.Empty, ReferenceDataCacheKeys.LeaveTypes))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Lookup_names_are_distinct_so_no_prefix_covers_another_lookup()
    {
        var names = typeof(ReferenceDataCacheKeys).GetFields()
            .Where(f => f.IsLiteral && f.Name != nameof(ReferenceDataCacheKeys.Root))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        names.Should().OnlyHaveUniqueItems();
        foreach (var name in names)
            names.Where(other => other != name)
                .Should().NotContain(other => ReferenceDataCacheKeys.NamePrefix(other).StartsWith(ReferenceDataCacheKeys.NamePrefix(name)));
    }
}
