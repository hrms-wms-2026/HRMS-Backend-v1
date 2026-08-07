using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class ModuleCatalogSeederTests
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

        return new ApplicationDbContext(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    [Fact]
    public async Task SeedAsync_SetsCanonicalOwnership_AndExcludesLegacyAndInvalid()
    {
        using var db = BuildInMemoryDb();
        
        // Seed permissions first so ownership references valid keys
        db.Permissions.AddRange(
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "time_off:read", Module = "time_off" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "time_off:create", Module = "time_off" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "time_off:approve", Module = "time_off" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "time_off:manage", Module = "time_off" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "calendar:read", Module = "calendar" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "monitoring:read", Module = "monitoring" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "monitoring:configure", Module = "monitoring" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "integrations:read", Module = "integrations" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "integrations:manage", Module = "integrations" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "leave:read", Module = "leave" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "notifications:manage", Module = "notifications" },
            // Still present as a leftover row in some pre-migration databases; the ownership map
            // no longer references it, so it must not get re-granted a module ownership row.
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "settings:notifications", Module = "configuration" }
        );
        await db.SaveChangesAsync();

        await ModuleCatalogSeeder.SeedAsync(db, CancellationToken.None);

        var ownerships = await db.ModulePermissionOwnerships.ToListAsync();

        // Prove ownership contains canonical
        ownerships.Should().Contain(o => o.ModuleKey == "time_off" && o.PermissionCode == "time_off:read");
        ownerships.Should().Contain(o => o.ModuleKey == "time_off" && o.PermissionCode == "time_off:create");
        ownerships.Should().Contain(o => o.ModuleKey == "time_off" && o.PermissionCode == "time_off:approve");
        ownerships.Should().Contain(o => o.ModuleKey == "time_off" && o.PermissionCode == "time_off:manage");
        ownerships.Should().Contain(o => o.ModuleKey == "calendar" && o.PermissionCode == "calendar:read");
        ownerships.Should().Contain(o => o.ModuleKey == "monitoring" && o.PermissionCode == "monitoring:read");
        ownerships.Should().Contain(o => o.ModuleKey == "monitoring" && o.PermissionCode == "monitoring:configure");

        // Prove ownership does NOT contain legacy or invalid monitoring keys
        ownerships.Should().NotContain(o => o.PermissionCode.StartsWith("leave:"));
        ownerships.Should().NotContain(o => o.PermissionCode == "monitoring:view");
        ownerships.Should().NotContain(o => o.PermissionCode == "monitoring:view-settings");
        ownerships.Should().NotContain(o => o.PermissionCode == "integrations:read");
        ownerships.Should().Contain(o =>
            o.ModuleKey == "integrations" &&
            o.PermissionCode == "integrations:manage");

        // settings:notifications is retired: notifications:manage is the sole canonical
        // notification-management permission owned by the notifications module.
        ownerships.Should().NotContain(o => o.PermissionCode == "settings:notifications");
        ownerships.Should().Contain(o =>
            o.ModuleKey == "notifications" &&
            o.PermissionCode == "notifications:manage");
    }
}
