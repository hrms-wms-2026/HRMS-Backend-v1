using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Services.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// DB-backed precedence tests for MonitoringToggleResolverService, seeding a real (in-memory)
/// ApplicationDbContext - mirrors the fixture style of MonitoringPolicyConfigurationServiceTests
/// and LocationRuleEvaluatorJobTests. The pure resolution-chain math itself (all six tiers, every
/// ordering combination) is covered without a DB in MonitoringToggleResolverTests; these tests
/// instead prove the service wires the new Work Mode tier - and the new radius resolver - to the
/// right rows: employee.WorkModeId, MonitoringPolicyOverride's "work_mode" scope, and the
/// employee/role/legal-entity precedence around it.
/// </summary>
public class MonitoringToggleResolverServiceTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    /// <summary>Never caches - every call re-resolves from the seeded DB, which is what these
    /// precedence tests want to exercise.</summary>
    private sealed class NoOpCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<Employee> SeedEmployeeAsync(
        ApplicationDbContext db, Guid tenantId, Guid? workModeId = null)
    {
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@resolver.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            WorkModeId = workModeId
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    private static async Task<Guid> SeedRoleForEmployeeAsync(ApplicationDbContext db, Guid tenantId, Guid userId)
    {
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = "resolver-test-role",
            Description = "Work Mode precedence fixture role",
            IsSystem = false,
            CreatedById = userId
        });
        db.UserRoles.Add(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedBy = userId
        });
        await db.SaveChangesAsync();
        return roleId;
    }

    private static async Task SeedWorkModePolicyOverrideAsync(
        ApplicationDbContext db, Guid tenantId, Guid workModeId,
        bool? workLocationVerification = null, int? idleThresholdMinutes = null, int? allowedRadiusMeters = null)
    {
        db.MonitoringPolicyOverrides.Add(new MonitoringPolicyOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScopeType = "work_mode",
            ScopeId = workModeId,
            WorkLocationVerification = workLocationVerification,
            IdleThresholdMinutes = idleThresholdMinutes,
            AllowedRadiusMeters = allowedRadiusMeters,
            SetById = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRolePolicyOverrideAsync(
        ApplicationDbContext db, Guid tenantId, Guid roleId,
        bool? workLocationVerification = null)
    {
        db.MonitoringPolicyOverrides.Add(new MonitoringPolicyOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScopeType = "role",
            ScopeId = roleId,
            WorkLocationVerification = workLocationVerification,
            SetById = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedLegalEntityDefaultAsync(
        ApplicationDbContext db, Guid tenantId, int? allowedRadiusMeters = null)
    {
        db.MonitoringFeatureToggles.Add(new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = null,
            AllowedRadiusMeters = allowedRadiusMeters
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ResolveAsync_WorkModeOverride_WinsOverRoleAndBelowEmployeeOverride()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var workModeId = Guid.NewGuid();

        var employee = await SeedEmployeeAsync(db, tenantId, workModeId);
        var roleId = await SeedRoleForEmployeeAsync(db, tenantId, employee.UserId);

        await SeedWorkModePolicyOverrideAsync(db, tenantId, workModeId, workLocationVerification: false);
        await SeedRolePolicyOverrideAsync(db, tenantId, roleId, workLocationVerification: true);

        var resolver = new MonitoringToggleResolverService(db, new NoOpCacheService());

        var enabled = await resolver.IsEnabledAsync(
            tenantId, employee.UserId, MonitoringCapability.WorkLocationVerification);

        enabled.Should().BeFalse("the work-mode-scoped override must win over the conflicting role-scoped override");
    }

    [Fact]
    public async Task ResolveAsync_EmployeeOverride_StillWinsOverWorkMode()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var workModeId = Guid.NewGuid();

        var employee = await SeedEmployeeAsync(db, tenantId, workModeId);
        var roleId = await SeedRoleForEmployeeAsync(db, tenantId, employee.UserId);

        await SeedWorkModePolicyOverrideAsync(db, tenantId, workModeId, workLocationVerification: false);
        await SeedRolePolicyOverrideAsync(db, tenantId, roleId, workLocationVerification: true);

        db.EmployeeMonitoringOverrides.Add(new EmployeeMonitoringOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            WorkLocationVerification = true,
            SetById = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var resolver = new MonitoringToggleResolverService(db, new NoOpCacheService());

        var enabled = await resolver.IsEnabledAsync(
            tenantId, employee.UserId, MonitoringCapability.WorkLocationVerification);

        enabled.Should().BeTrue("the employee-level override is still the topmost tier, above Work Mode");
    }

    [Fact]
    public async Task GetAllowedRadiusMetersAsync_FollowsSamePrecedenceAsIdleThreshold_UsingWorkModeTier()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var workModeId = Guid.NewGuid();

        var employee = await SeedEmployeeAsync(db, tenantId, workModeId);
        await SeedLegalEntityDefaultAsync(db, tenantId, allowedRadiusMeters: 500);
        await SeedWorkModePolicyOverrideAsync(db, tenantId, workModeId, allowedRadiusMeters: 150);

        var resolver = new MonitoringToggleResolverService(db, new NoOpCacheService());

        var radius = await resolver.GetAllowedRadiusMetersAsync(tenantId, employee.UserId);

        radius.Should().Be(150, "the work-mode-scoped radius override must win over the legal-entity default");
    }

    [Fact]
    public async Task GetAllowedRadiusMetersAsync_NoOverrideAnywhere_FallsBackToLegalEntityDefault()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();

        var employee = await SeedEmployeeAsync(db, tenantId, workModeId: null);
        await SeedLegalEntityDefaultAsync(db, tenantId, allowedRadiusMeters: 500);

        var resolver = new MonitoringToggleResolverService(db, new NoOpCacheService());

        var radius = await resolver.GetAllowedRadiusMetersAsync(tenantId, employee.UserId);

        radius.Should().Be(500, "with no employee/work-mode/role/position/department override, the legal-entity default applies");
    }

    [Fact]
    public async Task GetAllowedRadiusMetersAsync_GenuinelyNullResult_IsCachedAndNotRecomputed()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();

        // No legal-entity default, no overrides anywhere - resolves to a genuine null.
        var employee = await SeedEmployeeAsync(db, tenantId, workModeId: null);

        // A real cache (not the NoOp fake the other tests use), so this test can actually prove
        // caching behavior rather than just precedence math.
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var resolver = new MonitoringToggleResolverService(db, cache);

        var first = await resolver.GetAllowedRadiusMetersAsync(tenantId, employee.UserId);
        first.Should().BeNull();

        // Change the underlying data after the first call. If the null result were NOT cached
        // (the pre-fix bug: a bare `int?` cache entry can't distinguish "resolved to null" from
        // "nothing cached yet"), the second call would recompute against this new row and return
        // 500 instead of the cached null.
        await SeedLegalEntityDefaultAsync(db, tenantId, allowedRadiusMeters: 500);

        var second = await resolver.GetAllowedRadiusMetersAsync(tenantId, employee.UserId);

        second.Should().BeNull(
            "a genuinely-resolved null radius must be served from cache on the second call, " +
            "not recomputed against the now-changed DB state");
    }
}
