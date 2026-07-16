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
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.DevPlatform.Provisioning;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Tenancy;

public class ModuleEntitlementServiceTests
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

    private static ModuleEntitlementService BuildSut(ApplicationDbContext db) => new(db);

    private static TenantSubscription BuildSubscription(
        Guid tenantId,
        string status,
        string selectedModulesJson,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PlanId = Guid.NewGuid(),
            BillingCycle = "monthly",
            Status = status,
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CompanySizeRange = "51-200",
            SelectedModulesJson = selectedModulesJson,
            CreatedAt = createdAt
        };

    [Fact]
    public async Task GetEntitledPermissionsAsync_ReturnsPermissionsForModuleKeys()
    {
        var db = BuildInMemoryDb();
        db.Permissions.AddRange(
            new Permission { Id = Guid.NewGuid(), Code = "employees:read", Module = "employees", Description = "" },
            new Permission { Id = Guid.NewGuid(), Code = "leave:read", Module = "leave", Description = "" },
            new Permission { Id = Guid.NewGuid(), Code = "tasks:read", Module = "tasks", Description = "" }
        );
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.GetEntitledPermissionsAsync(["employees", "leave"]);

        result.Should().HaveCount(2);
        result.Select(p => p.Code).Should().BeEquivalentTo(["employees:read", "leave:read"]);
    }

    [Fact]
    public async Task GetEntitledPermissionsAsync_NoModules_ReturnsEmpty()
    {
        var db = BuildInMemoryDb();
        db.Permissions.AddRange(
            new Permission { Id = Guid.NewGuid(), Code = "tasks:read", Module = "tasks", Description = "" },
            new Permission { Id = Guid.NewGuid(), Code = "employees:read-own", Module = "employees", Description = "" }
        );
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.GetEntitledPermissionsAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEntitledPermissionsAsync_ReturnsEmptyWhenNoMatch()
    {
        var db = BuildInMemoryDb();
        db.Permissions.Add(
            new Permission { Id = Guid.NewGuid(), Code = "tasks:read", Module = "tasks", Description = "" }
        );
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.GetEntitledPermissionsAsync(["employees"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveModuleKeysForTenantAsync_ReturnsEmptyWhenNoSubscription()
    {
        var db = BuildInMemoryDb();
        var sut = BuildSut(db);

        var keys = await sut.GetActiveModuleKeysForTenantAsync(Guid.NewGuid());

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveModuleKeysForTenantAsync_ParsesSelectedModulesJson()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantSubscriptions.Add(
            BuildSubscription(tenantId, "active", """["employees","leave"]""", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var keys = await sut.GetActiveModuleKeysForTenantAsync(tenantId);

        keys.Should().Equal("employees", "leave");
    }

    [Fact]
    public async Task GetActiveModuleKeysForTenantAsync_PrefersLatestSubscriptionByCreatedAt()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantSubscriptions.Add(
            BuildSubscription(tenantId, "active", """["old"]""", DateTimeOffset.UtcNow.AddDays(-2)));
        db.TenantSubscriptions.Add(
            BuildSubscription(tenantId, "active", """["newer","leave"]""", DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var keys = await sut.GetActiveModuleKeysForTenantAsync(tenantId);

        keys.Should().Equal("newer", "leave");
    }

    [Fact]
    public async Task GetAssignablePermissionsForTenantAsync_ReturnsOnlyEntitledNonUniversalNonWildcard()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantSubscriptions.Add(
            BuildSubscription(tenantId, "trialing", """["leave"]""", DateTimeOffset.UtcNow));

        var leaveRead = Guid.NewGuid();
        var payrollRun = Guid.NewGuid();
        var wildcard = Guid.NewGuid();
        var universalId = Guid.NewGuid();
        db.Permissions.AddRange(
            new Permission { Id = leaveRead, Code = "leave:read", Module = "leave", Description = "" },
            new Permission { Id = payrollRun, Code = "payroll:run", Module = "payroll", Description = "" },
            new Permission { Id = wildcard, Code = "*", Module = "system", Description = "" },
            new Permission
            {
                Id = universalId,
                Code = "employees:read-own",
                Module = "employees",
                Description = ""
            }
        );
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.GetAssignablePermissionsForTenantAsync(tenantId);

        result.Should().ContainSingle();
        result[0].Code.Should().Be("leave:read");
    }

    [Fact]
    public async Task GetAssignablePermissionsForTenantAsync_ReturnsEmptyWhenNoActiveSubscription()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Permissions.Add(
            new Permission { Id = Guid.NewGuid(), Code = "leave:read", Module = "leave", Description = "" });
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.GetAssignablePermissionsForTenantAsync(tenantId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssignablePermissionsForTenantAsync_WithPermissionIds_FiltersToAssignableOnly()
    {
        var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantSubscriptions.Add(
            BuildSubscription(tenantId, "active", """["leave"]""", DateTimeOffset.UtcNow));

        var leaveRead = Guid.NewGuid();
        var payrollRun = Guid.NewGuid();
        db.Permissions.AddRange(
            new Permission { Id = leaveRead, Code = "leave:read", Module = "leave", Description = "" },
            new Permission { Id = payrollRun, Code = "payroll:run", Module = "payroll", Description = "" }
        );
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var result = await sut.GetAssignablePermissionsForTenantAsync(
            tenantId,
            [leaveRead, payrollRun]);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(leaveRead);
    }
}
