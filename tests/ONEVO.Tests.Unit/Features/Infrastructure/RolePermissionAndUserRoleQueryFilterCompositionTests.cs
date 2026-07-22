using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// Proves ApplicationDbContext.OnModelCreating composes the generic
/// tenant/soft-delete query filter with entity-specific filters instead of
/// overwriting them. UserRoleConfiguration and RolePermissionConfiguration
/// each declare `HasQueryFilter(x => !x.Role.IsDeleted)` via
/// IEntityTypeConfiguration, which runs before the generic tenant-filter loop
/// in ApplicationDbContext. Before composition, the generic loop's own
/// unconditional HasQueryFilter call replaced that predicate, so a role
/// assignment or permission grant attached to a soft-deleted role stayed
/// visible.
/// </summary>
public class RolePermissionAndUserRoleQueryFilterCompositionTests
{
    private static readonly DbContextOptions<ApplicationDbContext> SharedOptions =
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("role_permission_user_role_filter_composition_test")
            .UseSnakeCaseNamingConvention()
            .Options;

    [Fact]
    public async Task UserRoleQueryFilter_HidesAssignmentToSoftDeletedRole_ButKeepsAssignmentToActiveRole()
    {
        var tenantId = Guid.NewGuid();
        var activeRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Active Role", IsDeleted = false };
        var deletedRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Deleted Role", IsDeleted = true };
        var userId = Guid.NewGuid();

        await using (var seedDb = CreateContext(new TenantContextAccessor()))
        {
            seedDb.Roles.AddRange(activeRole, deletedRole);
            seedDb.UserRoles.AddRange(
                new UserRole { TenantId = tenantId, UserId = userId, RoleId = activeRole.Id, AssignedBy = userId },
                new UserRole { TenantId = tenantId, UserId = userId, RoleId = deletedRole.Id, AssignedBy = userId });
            await seedDb.SaveChangesAsync();
        }

        var tenantContext = new TenantContextAccessor();
        tenantContext.Resolve(new TenantRegistryEntry(tenantId, "tenant-a", TenantStatus.Active, null));
        await using var db = CreateContext(tenantContext);

        var visibleAssignments = await db.UserRoles.ToListAsync();

        visibleAssignments.Should().ContainSingle();
        visibleAssignments.Single().RoleId.Should().Be(activeRole.Id);
    }

    [Fact]
    public async Task RolePermissionQueryFilter_HidesGrantOnSoftDeletedRole_ButKeepsGrantOnActiveRole()
    {
        var tenantId = Guid.NewGuid();
        var activeRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Active Role", IsDeleted = false };
        var deletedRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Deleted Role", IsDeleted = true };
        var permission = new Permission { Id = Guid.NewGuid(), Code = "employees.read" };

        await using (var seedDb = CreateContext(new TenantContextAccessor()))
        {
            seedDb.Roles.AddRange(activeRole, deletedRole);
            seedDb.Permissions.Add(permission);
            seedDb.RolePermissions.AddRange(
                new RolePermission { TenantId = tenantId, RoleId = activeRole.Id, PermissionId = permission.Id },
                new RolePermission { TenantId = tenantId, RoleId = deletedRole.Id, PermissionId = permission.Id });
            await seedDb.SaveChangesAsync();
        }

        var tenantContext = new TenantContextAccessor();
        tenantContext.Resolve(new TenantRegistryEntry(tenantId, "tenant-a", TenantStatus.Active, null));
        await using var db = CreateContext(tenantContext);

        var visibleGrants = await db.RolePermissions.ToListAsync();

        visibleGrants.Should().ContainSingle();
        visibleGrants.Single().RoleId.Should().Be(activeRole.Id);
    }

    [Fact]
    public async Task TenantAContext_CannotSee_TenantBUserRoles()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var roleA = new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Tenant A Role" };
        var roleB = new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Tenant B Role" };
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var seedDb = CreateContext(new TenantContextAccessor()))
        {
            seedDb.Roles.AddRange(roleA, roleB);
            seedDb.UserRoles.AddRange(
                new UserRole { TenantId = tenantA, UserId = userA, RoleId = roleA.Id, AssignedBy = userA },
                new UserRole { TenantId = tenantB, UserId = userB, RoleId = roleB.Id, AssignedBy = userB });
            await seedDb.SaveChangesAsync();
        }

        var tenantAContext = new TenantContextAccessor();
        tenantAContext.Resolve(new TenantRegistryEntry(tenantA, "tenant-a", TenantStatus.Active, null));
        await using var dbA = CreateContext(tenantAContext);

        var visibleToTenantA = await dbA.UserRoles.ToListAsync();

        visibleToTenantA.Should().ContainSingle();
        visibleToTenantA.Single().TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task TenantAContext_CannotSee_TenantBRolePermissions()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var roleA = new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Tenant A Role" };
        var roleB = new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Tenant B Role" };
        var permission = new Permission { Id = Guid.NewGuid(), Code = "employees.read" };

        await using (var seedDb = CreateContext(new TenantContextAccessor()))
        {
            seedDb.Roles.AddRange(roleA, roleB);
            seedDb.Permissions.Add(permission);
            seedDb.RolePermissions.AddRange(
                new RolePermission { TenantId = tenantA, RoleId = roleA.Id, PermissionId = permission.Id },
                new RolePermission { TenantId = tenantB, RoleId = roleB.Id, PermissionId = permission.Id });
            await seedDb.SaveChangesAsync();
        }

        var tenantAContext = new TenantContextAccessor();
        tenantAContext.Resolve(new TenantRegistryEntry(tenantA, "tenant-a", TenantStatus.Active, null));
        await using var dbA = CreateContext(tenantAContext);

        var visibleToTenantA = await dbA.RolePermissions.ToListAsync();

        visibleToTenantA.Should().ContainSingle();
        visibleToTenantA.Single().TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task TwoDbContextInstances_WithDifferentTenants_SharedCachedModel_UserRoleAndRolePermission_OnlySeeOwnTenantRows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var roleA = new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Tenant A Role" };
        var roleB = new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Tenant B Role" };
        var permission = new Permission { Id = Guid.NewGuid(), Code = "employees.read" };
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // Seeded through a System-mode context first, exactly mirroring how
        // ApplicationDbContextFactory (migrations) constructs its own
        // unresolved TenantContextAccessor - the instance whose ITenantContext
        // the old buggy closure pattern would have frozen into the shared
        // compiled model (this test uses SharedOptions, so all contexts below
        // reuse the exact same compiled EF model).
        await using (var seedDb = CreateContext(new TenantContextAccessor()))
        {
            seedDb.Roles.AddRange(roleA, roleB);
            seedDb.Permissions.Add(permission);
            seedDb.UserRoles.AddRange(
                new UserRole { TenantId = tenantA, UserId = userA, RoleId = roleA.Id, AssignedBy = userA },
                new UserRole { TenantId = tenantB, UserId = userB, RoleId = roleB.Id, AssignedBy = userB });
            seedDb.RolePermissions.AddRange(
                new RolePermission { TenantId = tenantA, RoleId = roleA.Id, PermissionId = permission.Id },
                new RolePermission { TenantId = tenantB, RoleId = roleB.Id, PermissionId = permission.Id });
            await seedDb.SaveChangesAsync();
        }

        var tenantAContext = new TenantContextAccessor();
        tenantAContext.Resolve(new TenantRegistryEntry(tenantA, "tenant-a", TenantStatus.Active, null));
        await using var dbA = CreateContext(tenantAContext);

        var tenantBContext = new TenantContextAccessor();
        tenantBContext.Resolve(new TenantRegistryEntry(tenantB, "tenant-b", TenantStatus.Active, null));
        await using var dbB = CreateContext(tenantBContext);

        var userRolesForA = await dbA.UserRoles.ToListAsync();
        var userRolesForB = await dbB.UserRoles.ToListAsync();
        var rolePermissionsForA = await dbA.RolePermissions.ToListAsync();
        var rolePermissionsForB = await dbB.RolePermissions.ToListAsync();

        userRolesForA.Should().HaveCount(1);
        userRolesForA.Should().OnlyContain(ur => ur.TenantId == tenantA);
        userRolesForB.Should().HaveCount(1);
        userRolesForB.Should().OnlyContain(ur => ur.TenantId == tenantB);

        rolePermissionsForA.Should().HaveCount(1);
        rolePermissionsForA.Should().OnlyContain(rp => rp.TenantId == tenantA);
        rolePermissionsForB.Should().HaveCount(1);
        rolePermissionsForB.Should().OnlyContain(rp => rp.TenantId == tenantB);
    }

    private static ApplicationDbContext CreateContext(ITenantContext tenantContext) =>
        new ApplicationDbContext(
            SharedOptions,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
}
