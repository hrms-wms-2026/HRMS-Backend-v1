using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.SharedPlatform;

namespace ONEVO.Tests.Unit.Features.Tenancy;

/// <summary>
/// Characterizes how DefaultRoleSeeder grants module-owned permissions (including
/// org:read/org:manage and roles:read/roles:manage) to the per-tenant "Owner" role -
/// the actual tenant-owner/organization-admin mechanism in this codebase (RoleTemplate,
/// by contrast, is a separate Developer-Platform operator artifact and is not exercised
/// here).
///
/// As of PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md, the seeded
/// "starter_51_200" plan's included_modules_json holds only real Phase 1 product
/// package modules (org_structure, core_hr, leave, calendar, ... - never platform
/// capability keys like "auth"/"configuration"/"roles"/"notifications"). org:read/
/// org:manage now flow to Owner through normal per-module entitlement because
/// "org_structure" is one of those product modules. roles:read/roles:manage cannot -
/// "roles" is a platform capability, never a subscribed product module - so
/// DefaultRoleSeeder grants a fixed baseline permission set (owned by the
/// PlatformBaselineModules.Keys module list) to every tenant's Owner role
/// unconditionally, independent of which product modules the plan includes.
/// </summary>
public sealed class DefaultRoleSeederTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    private static async Task SeedOrgPermissionsAsync(ApplicationDbContext db)
    {
        db.Permissions.AddRange(
            new Permission { Id = Guid.NewGuid(), Code = "org:read", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "org:manage", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "legal_entity:create", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "legal_entity:update", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "legal_entity:delete", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "roles:read", Module = "roles" },
            new Permission { Id = Guid.NewGuid(), Code = "roles:manage", Module = "roles" },
            new Permission { Id = Guid.NewGuid(), Code = "settings:read", Module = "configuration" },
            new Permission { Id = Guid.NewGuid(), Code = "users:read", Module = "auth" },
            new Permission { Id = Guid.NewGuid(), Code = "notifications:manage", Module = "notifications" },
            new Permission { Id = Guid.NewGuid(), Code = "employees:read", Module = "core_hr" },
            new Permission { Id = Guid.NewGuid(), Code = "*", Module = "system" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_GrantsOrgPermissionsToOwner_WhenOrgStructureModuleIncluded()
    {
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, ["core_hr", "worksync_foundation", "org_structure"], CancellationToken.None);

        var grantedPermissionIds = owner.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var orgPermissionIds = await db.Permissions
            .Where(p => p.Code == "org:read" || p.Code == "org:manage")
            .Select(p => p.Id)
            .ToListAsync();

        grantedPermissionIds.Should().Contain(orgPermissionIds);
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_GrantsLegalEntityPermissionsToOwner_WhenOrgStructureModuleIncluded()
    {
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, ["core_hr", "worksync_foundation", "org_structure"], CancellationToken.None);

        var grantedPermissionIds = owner.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var legalEntityPermissionIds = await db.Permissions
            .Where(p => p.Code == "legal_entity:create" || p.Code == "legal_entity:update" || p.Code == "legal_entity:delete")
            .Select(p => p.Id)
            .ToListAsync();

        grantedPermissionIds.Should().Contain(legalEntityPermissionIds);
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_DoesNotGrantOrgPermissionsToOwner_WhenOrgStructureModuleNotIncluded()
    {
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, ["core_hr", "worksync_foundation"], CancellationToken.None);

        var grantedPermissionIds = owner.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var orgPermissionIds = await db.Permissions
            .Where(p => p.Code == "org:read" || p.Code == "org:manage")
            .Select(p => p.Id)
            .ToListAsync();

        grantedPermissionIds.Should().NotContain(orgPermissionIds);
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_ExcludesWildcardPermission_EvenWhenItsModuleIsIncluded()
    {
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, ["system", "org_structure"], CancellationToken.None);

        var wildcardId = await db.Permissions.Where(p => p.Code == "*").Select(p => p.Id).SingleAsync();
        owner.RolePermissions.Select(rp => rp.PermissionId).Should().NotContain(wildcardId);
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_EmployeeRoleReceivesNoExplicitPermissions()
    {
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var roles = await seeder.SeedDefaultRolesAsync(tenantId, ["core_hr", "worksync_foundation", "org_structure"], CancellationToken.None);
        var employee = roles.Single(r => r.Name == "Employee");

        employee.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_GrantsOrgAndRolesPermissionsToOwner_WithTheActualSeededStarterPlanModuleList()
    {
        // Mirrors the real included_modules_json value the "starter_51_200" plan carries
        // per PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md: only real Phase 1
        // product package modules, never platform capability keys ("auth"/"configuration"/
        // "roles"/"notifications" are absent). org:read/org:manage still reach Owner because
        // "org_structure" is a genuine product module in this list. roles:read/roles:manage
        // reach Owner anyway, via DefaultRoleSeeder's unconditional baseline-permission grant
        // (PlatformBaselineModules) - not because "roles" is in this list, since it isn't.
        // This is the regression guard for the module-list drop that caused
        // TenantProvisioningE2ETests to 403 on GET /api/v1/roles.
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();
        var seededStarterPlanModules = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
            """["org_structure","core_hr","leave","calendar","time_attendance","activity_monitoring","discrepancy_engine","identity_verification","exception_engine","productivity_analytics","desktop_agent_gateway","worksync_foundation","projects","objectives_milestones","tasks","boards","planning_sprints"]""")!;

        seededStarterPlanModules.Should().NotContain(["auth", "configuration", "roles", "notifications"]);

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, seededStarterPlanModules, CancellationToken.None);

        var grantedCodes = await db.Permissions
            .Where(p => owner.RolePermissions.Select(rp => rp.PermissionId).Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync();
        grantedCodes.Should().Contain(["org:read", "org:manage", "roles:read", "roles:manage"]);
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_GrantsBaselinePlatformPermissionsToOwner_RegardlessOfSubscribedModules()
    {
        // The baseline-bootstrap mechanism this task introduces: roles:read/roles:manage,
        // settings:read (configuration), users:read (auth), and notifications:manage must
        // reach every tenant's Owner role even when none of "roles"/"configuration"/"auth"/
        // "notifications" appear in the tenant's subscribed product-module list, because
        // those are platform capabilities, not subscribed Phase 1 product package modules.
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, ["core_hr"], CancellationToken.None);

        var grantedCodes = await db.Permissions
            .Where(p => owner.RolePermissions.Select(rp => rp.PermissionId).Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync();
        grantedCodes.Should().Contain(["roles:read", "roles:manage", "settings:read", "users:read", "notifications:manage"]);
    }
}
