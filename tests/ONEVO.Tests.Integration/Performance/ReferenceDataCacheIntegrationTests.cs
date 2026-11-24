using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Diagnostics;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Type;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using Xunit.Abstractions;

namespace ONEVO.Tests.Integration.Performance;

/// <summary>
/// Improvements task 22: the ten cached reference lookups, counted on real SQL Server with the task 06 request counter
/// (<see cref="SqlCommandMetricsInterceptor"/>). Each "request" is a new DbContext, as in the API; the process cache is
/// shared between requests. With caching switched off (DEC-105) the lookups behave as before task 22. Requires Docker.
/// </summary>
public sealed class ReferenceDataCacheIntegrationTests(ITestOutputHelper output)
{
    private const string LeaveModule = "leave";
    private const string WorkModule = "work_management";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private sealed class Options(bool enabled) : IOptionsMonitor<ReferenceDataCacheOptions>
    {
        public ReferenceDataCacheOptions CurrentValue { get; } = new() { Enabled = enabled, LifetimeMinutes = 5 };
        public ReferenceDataCacheOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ReferenceDataCacheOptions, string?> listener) => null;
    }

    /// <summary>One API request: its own DbContext (resolved to a tenant) with the task 06 counter, over a shared cache.</summary>
    private sealed class Request(ApplicationDbContext db, RequestSqlMetrics metrics, TenantCache cache) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public RequestSqlMetrics Metrics { get; } = metrics;
        public TenantCache Cache { get; } = cache;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed record World(MyTeamDb Helper, Guid ProjectId);

    private static MemoryCacheService NewProcessCache() => new(new MemoryCache(new MemoryCacheOptions()));

    private static Request Open(MyTeamDb helper, MemoryCacheService processCache, bool enabled = true, Guid? resolvedTenant = null)
    {
        var metrics = new RequestSqlMetrics();
        var tenantId = resolvedTenant ?? helper.TenantId;
        var db = helper.NewTenantContext(tenantId, new SqlCommandMetricsInterceptor(metrics));
        var tenantContext = new TenantContextAccessor();
        tenantContext.Resolve(new TenantRegistryEntry(tenantId, "my-team", TenantStatus.Active, PlanCode: null));
        return new Request(db, metrics, new TenantCache(processCache, tenantContext, new Options(enabled)));
    }

    /// <summary>A writing context with the task 22 invalidation interceptor, as registered in the API.</summary>
    private static ApplicationDbContext Writer(MyTeamDb helper, MemoryCacheService processCache) =>
        helper.NewContext(new ReferenceDataCacheInvalidationInterceptor(processCache));

    private static async Task<World> SeedAsync(MyTeamDb helper, string module)
    {
        await using var db = helper.NewContext();
        var now = DateTimeOffset.UtcNow;
        db.Add(new TenantSubscription
        {
            Id = Guid.NewGuid(), TenantId = helper.TenantId, PlanId = SeededPlanId, Status = "active",
            SelectedModulesJson = $"[\"{module}\"]",
            CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime),
            CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime), CreatedAt = now,
        });
        if (!await db.Permissions.AnyAsync(p => p.Code == "t22:" + module))
            db.Permissions.Add(new Permission { Id = Guid.NewGuid(), Code = "t22:" + module, Module = module, Description = "task 22" });
        if (!await db.ModuleCatalog.AnyAsync(m => m.ModuleKey == "t22-module"))
            db.ModuleCatalog.Add(new ModuleCatalogItem { ModuleKey = "t22-module", Name = "Task 22", Pillar = "test" });
        helper.AddLeaveType(db);
        helper.AddClockInPolicy(db);
        db.Add(new WorkMode
        {
            Id = Guid.NewGuid(), TenantId = helper.TenantId, LegalEntityId = helper.LegalEntityId, Name = "Office",
            WebEnabled = true, CreatedAt = now, UpdatedAt = now,
        });
        db.Add(new HolidayCalendarSettings
        {
            Id = Guid.NewGuid(), TenantId = helper.TenantId, LegalEntityId = helper.LegalEntityId, DefaultCountryCode = "LK",
        });
        var lead = helper.AddEmployee(db);
        var (projectId, _) = helper.AddProject(db, lead.Id);
        await db.SaveChangesAsync();
        return new World(helper, projectId);
    }

    /// <summary>The ten lookups, each run once by a request.</summary>
    private static IReadOnlyList<(string Name, Func<Request, World, Task> Run)> Lookups() =>
    [
        ("Tenant active modules (ModuleEntitlementService)",
            (r, w) => new ModuleEntitlementService(r.Db, r.Cache).GetActiveModuleKeysForTenantAsync(w.Helper.TenantId)),
        ("Assignable permissions (ModuleEntitlementService)",
            (r, w) => new ModuleEntitlementService(r.Db, r.Cache).GetAssignablePermissionsForTenantAsync(w.Helper.TenantId)),
        ("Permission catalogue incl. universal rows (TenantPermissionCatalogService)",
            (r, w) => new TenantPermissionCatalogService(new ModuleEntitlementService(r.Db, r.Cache), r.Db, r.Cache)
                .GetCatalogAsync(w.Helper.TenantId)),
        ("Active module catalogue (ModuleCatalogService)",
            (r, _) => new ModuleCatalogService(r.Db, r.Cache).GetActiveModuleKeysAsync()),
        ("Leave types (EfLeaveTypeRepository.ListAsync)",
            (r, w) => new EfLeaveTypeRepository(r.Db, r.Cache).ListAsync(w.Helper.TenantId, includeInactive: false)),
        ("Work modes (EfWorkModeRepository.ListByLegalEntityAsync)",
            (r, w) => new EfWorkModeRepository(r.Db, r.Cache).ListByLegalEntityAsync(w.Helper.TenantId, w.Helper.LegalEntityId, false)),
        ("Clock-in policies (EfClockInPolicyRepository.ListActiveOnDateAsync)",
            (r, w) => new EfClockInPolicyRepository(r.Db, r.Cache).ListActiveOnDateAsync(
                w.Helper.TenantId, w.Helper.LegalEntityId, DateOnly.FromDateTime(DateTime.UtcNow))),
        ("Holiday calendar settings (EfHolidayCalendarSettingsRepository)",
            (r, w) => new EfHolidayCalendarSettingsRepository(r.Db, r.Cache).GetByLegalEntityAsync(w.Helper.TenantId, w.Helper.LegalEntityId)),
        ("Task categories (EfTaskCategoryRepository.GetByProjectIdAsync)",
            (r, w) => new EfTaskCategoryRepository(r.Db, r.Cache).GetByProjectIdAsync(w.Helper.TenantId, w.ProjectId)),
        ("Project categories (EfProjectCategoryRepository.GetAllForTenantAsync)",
            (r, w) => new EfProjectCategoryRepository(r.Db, r.Cache).GetAllForTenantAsync(w.Helper.TenantId)),
    ];

    private static async Task<int> CountAsync(World world, MemoryCacheService processCache, bool enabled, Func<Request, World, Task> run)
    {
        await using var request = Open(world.Helper, processCache, enabled);
        await run(request, world);
        return request.Metrics.CommandCount;
    }

    [Fact]
    public async Task Each_cached_lookup_runs_no_query_on_a_repeat_request()
    {
        var world = await SeedAsync(await MyTeamDb.CreateAsync(), LeaveModule);
        output.WriteLine("| Lookup | Off: 1st | Off: repeat | On: 1st | On: repeat |");
        output.WriteLine("|---|---|---|---|---|");

        foreach (var (name, run) in Lookups())
        {
            var off = NewProcessCache();
            var offFirst = await CountAsync(world, off, enabled: false, run);
            var offRepeat = await CountAsync(world, off, enabled: false, run);
            var on = NewProcessCache();
            var onFirst = await CountAsync(world, on, enabled: true, run);
            var onRepeat = await CountAsync(world, on, enabled: true, run);
            output.WriteLine($"| {name} | {offFirst} | {offRepeat} | {onFirst} | {onRepeat} |");

            offRepeat.Should().Be(offFirst, $"{name}: with caching off every request queries");
            offFirst.Should().BeGreaterThan(0, $"{name}: the lookup reads the database");
            onFirst.Should().Be(offFirst, $"{name}: the first request fills the cache with the same queries");
            onRepeat.Should().Be(0, $"{name}: a repeat request is served from the cache");
        }
    }

    [Fact]
    public async Task A_saved_change_is_visible_on_the_very_next_read()
    {
        var world = await SeedAsync(await MyTeamDb.CreateAsync(), LeaveModule);
        var helper = world.Helper;
        var cache = NewProcessCache();

        async Task<(IReadOnlyList<string> Modules, int LeaveTypes, IReadOnlyList<string> Categories, int Permissions)> ReadAsync()
        {
            await using var r = Open(helper, cache);
            var entitlements = new ModuleEntitlementService(r.Db, r.Cache);
            return (
                await entitlements.GetActiveModuleKeysForTenantAsync(helper.TenantId),
                (await new EfLeaveTypeRepository(r.Db, r.Cache).ListAsync(helper.TenantId, false)).Count,
                (await new EfTaskCategoryRepository(r.Db, r.Cache).GetByProjectIdAsync(helper.TenantId, world.ProjectId))
                    .Select(c => c.Name).ToList(),
                (await entitlements.GetAssignablePermissionsForTenantAsync(helper.TenantId)).Count);
        }

        var before = await ReadAsync();
        (await ReadAsync()).Should().BeEquivalentTo(before, "the second read is served from the cache");

        // Tracked writes, as every command handler makes them.
        await using (var db = Writer(helper, cache))
        {
            var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == helper.TenantId);
            subscription.SelectedModulesJson = $"[\"{LeaveModule}\",\"{WorkModule}\"]";
            db.LeaveTypes.Add(new LeaveType
            {
                Id = Guid.NewGuid(), TenantId = helper.TenantId, Name = "Sick", Code = $"SK{Guid.NewGuid():N}"[..8],
                IsPaid = true, ApplicableGender = LeaveGenderRestrictions.All,
            });
            await db.SaveChangesAsync();
        }

        // Inside an explicit transaction (unit of work): the entries go again on commit.
        await using (var db = Writer(helper, cache))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var category = await db.TaskCategories.SingleAsync(c => c.ProjectId == world.ProjectId);
            category.Name = "Renamed";
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // A platform-wide change: a new permission in an active module.
        await using (var db = Writer(helper, cache))
        {
            db.Permissions.Add(new Permission { Id = Guid.NewGuid(), Code = $"t22:new:{Guid.NewGuid():N}", Module = LeaveModule, Description = "new" });
            await db.SaveChangesAsync();
        }

        var after = await ReadAsync();
        after.Modules.Should().BeEquivalentTo([LeaveModule, WorkModule]);
        after.LeaveTypes.Should().Be(before.LeaveTypes + 1);
        after.Categories.Should().Equal("Renamed");
        after.Permissions.Should().BeGreaterThan(before.Permissions);
    }

    [Fact]
    public async Task Two_tenants_never_share_an_entry()
    {
        var a = await SeedAsync(await MyTeamDb.CreateAsync(), LeaveModule);
        var b = await SeedAsync(await a.Helper.AddTenantAsync(), WorkModule);
        var cache = NewProcessCache();

        async Task<(IReadOnlyList<string> Modules, IReadOnlyList<Guid> LeaveTypeTenants)> ReadAsync(MyTeamDb helper, Guid? resolved = null)
        {
            await using var r = Open(helper, cache, resolvedTenant: resolved);
            return (
                await new ModuleEntitlementService(r.Db, r.Cache).GetActiveModuleKeysForTenantAsync(helper.TenantId),
                (await new EfLeaveTypeRepository(r.Db, r.Cache).ListAsync(helper.TenantId, false)).Select(t => t.TenantId).ToList());
        }

        var readA = await ReadAsync(a.Helper);
        var readB = await ReadAsync(b.Helper);
        readA = await ReadAsync(a.Helper); // now from the cache

        readA.Modules.Should().Equal(LeaveModule);
        readA.LeaveTypeTenants.Should().OnlyContain(id => id == a.Helper.TenantId).And.NotBeEmpty();
        readB.Modules.Should().Equal(WorkModule);
        readB.LeaveTypeTenants.Should().OnlyContain(id => id == b.Helper.TenantId).And.NotBeEmpty();

        // A request resolved to tenant A asking for tenant B's rows sees none (RLS) and must not cache that.
        var crossCache = NewProcessCache();
        await using (var cross = Open(a.Helper, crossCache))
            (await new EfLeaveTypeRepository(cross.Db, cross.Cache).ListAsync(b.Helper.TenantId, false)).Should().BeEmpty();
        await using (var own = Open(b.Helper, crossCache))
        {
            (await new EfLeaveTypeRepository(own.Db, own.Cache).ListAsync(b.Helper.TenantId, false)).Should().NotBeEmpty();
            own.Metrics.CommandCount.Should().BeGreaterThan(0, "tenant B's entry was not filled by tenant A's request");
        }
    }
}
