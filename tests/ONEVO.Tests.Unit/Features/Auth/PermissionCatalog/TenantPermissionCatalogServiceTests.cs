using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.DevPlatform.Provisioning;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.PermissionCatalog;

public class TenantPermissionCatalogServiceTests
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

        return new ApplicationDbContext(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new TenantContextAccessor());
    }

    [Fact]
    public async Task GetCatalogAsync_AssignableComesFromEntitlementService_AndExcludesStar()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var leaveId = Guid.NewGuid();
        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Permission
                {
                    Id = leaveId,
                    Code = "leave:read",
                    Module = "leave",
                    Description = "Read leave"
                }
            ]);

        var sut = new TenantPermissionCatalogService(entitlements.Object, db);
        var catalog = await sut.GetCatalogAsync(tenantId);

        catalog.AssignablePermissions.Should().ContainSingle();
        var a = catalog.AssignablePermissions[0];
        a.Code.Should().Be("leave:read");
        a.IsAssignable.Should().BeTrue();
        a.IsUniversal.Should().BeFalse();
        catalog.AssignablePermissions.Should().NotContain(x => x.Code == "*");
    }

    [Fact]
    public async Task GetCatalogAsync_UniversalSection_IsNotAssignable_AndIncludesAllUniversalCodes()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = new TenantPermissionCatalogService(entitlements.Object, db);
        var catalog = await sut.GetCatalogAsync(tenantId);

        catalog.UniversalPermissions.Should().HaveCount(
            ModuleAutoGrants.ByModule.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count());
        catalog.UniversalPermissions.Should().OnlyContain(u => u.IsUniversal && !u.IsAssignable);
        catalog.UniversalPermissions.Should().NotContain(u => u.Code == "*");
    }

    [Fact]
    public async Task GetCatalogAsync_UsesDbMetadataForUniversal_WhenPermissionRowExists()
    {
        var db = BuildInMemoryDb();
        var autoGrantId = Guid.NewGuid();
        db.Permissions.Add(new Permission
        {
            Id = autoGrantId,
            Code = "employees:read-own",
            Module = "employees",
            Description = "From seed"
        });
        await db.SaveChangesAsync();

        var tenantId = Guid.NewGuid();
        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = new TenantPermissionCatalogService(entitlements.Object, db);
        var catalog = await sut.GetCatalogAsync(tenantId);

        var autoGrant = catalog.UniversalPermissions.Single(u => u.Code == "employees:read-own");
        autoGrant.Id.Should().Be(autoGrantId);
        autoGrant.Description.Should().Be("From seed");
        autoGrant.Module.Should().Be("employees");
    }

    [Fact]
    public async Task GetCatalogAsync_WithRealEntitlements_UniversalCodesOnlyInUniversalSection()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PlanId = Guid.NewGuid(),
            BillingCycle = "monthly",
            Status = "active",
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CompanySizeRange = "51-200",
            SelectedModulesJson = """["leave"]""",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Code = "leave:read",
            Module = "leave",
            Description = ""
        });
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Code = "employees:read-own",
            Module = "employees",
            Description = "universal row"
        });
        await db.SaveChangesAsync();

        var entitlements = new ModuleEntitlementService(db);
        var sut = new TenantPermissionCatalogService(entitlements, db);
        var catalog = await sut.GetCatalogAsync(tenantId);

        catalog.AssignablePermissions.Should().ContainSingle().Which.Code.Should().Be("leave:read");
        catalog.UniversalPermissions.Should().Contain(u => u.Code == "employees:read-own");
        catalog.AssignablePermissions.Should().NotContain(u => u.Code == "employees:read-own");
    }
}
