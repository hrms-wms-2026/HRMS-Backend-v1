using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Confirms org:read/org:manage exist as canonical permissions owned by the
/// "org_structure" module (the Phase 1 HR product module - see
/// PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md; renamed from "org" so
/// these permissions flow through normal per-module entitlement instead of a
/// platform-capability key living in included_modules_json).
/// See DefaultRoleSeederTests (Features.Tenancy) for how those permissions actually
/// reach a tenant's Owner role, and DevSmokeTestTenantSeederTests for proof that the
/// dev-seeded "Tenant Owner"/"HR Manager" smoke users already receive both.
/// </summary>
public class OrgPermissionSeedTests
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

    [Fact]
    public async Task PermissionSeeder_SeedsOrgReadAndOrgManage_OwnedByOrgModule()
    {
        using var db = BuildInMemoryDb();
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var sp = services.BuildServiceProvider();
        var seeder = new PermissionSeeder(sp, NullLogger<PermissionSeeder>.Instance);
        var method = typeof(PermissionSeeder).GetMethod(
            "SeedPermissionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(seeder, [db, CancellationToken.None])!;

        var orgRead = await db.Permissions.SingleAsync(p => p.Code == "org:read");
        var orgManage = await db.Permissions.SingleAsync(p => p.Code == "org:manage");

        orgRead.Module.Should().Be("org_structure");
        orgManage.Module.Should().Be("org_structure");
    }

    [Fact]
    public async Task ModuleCatalogSeeder_OwnsOrgReadAndOrgManage_UnderOrgStructureModule()
    {
        using var db = BuildInMemoryDb();
        db.Permissions.AddRange(
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "org:read", Module = "org_structure" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "org:manage", Module = "org_structure" });
        await db.SaveChangesAsync();

        await ModuleCatalogSeeder.SeedAsync(db, CancellationToken.None);

        var ownerships = await db.ModulePermissionOwnerships.ToListAsync();

        ownerships.Should().Contain(o => o.ModuleKey == "org_structure" && o.PermissionCode == "org:read");
        ownerships.Should().Contain(o => o.ModuleKey == "org_structure" && o.PermissionCode == "org:manage");
    }
}
