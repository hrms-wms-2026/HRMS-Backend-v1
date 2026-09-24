using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class PermissionSeederTests
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
    public async Task SeedAsync_SeedsCanonicalPermissions_AndDoesNotSeedInvalidMonitoring()
    {
        using var db = BuildInMemoryDb();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped(sp => db);
        var sp = services.BuildServiceProvider();

        var seeder = new PermissionSeeder(sp, new NullLogger<PermissionSeeder>());
        
        // We cannot call StartAsync on InMemory DB due to MigrateAsync failure. 
        // We need to use reflection to call SeedPermissionsAsync.
        var method = typeof(PermissionSeeder).GetMethod("SeedPermissionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method.Invoke(seeder, new object[] { db, CancellationToken.None });

        var codes = await db.Permissions.Select(p => p.Code).ToListAsync();

        // Must contain canonical
        codes.Should().Contain([
            "time_off:read",
            "time_off:create",
            "time_off:approve",
            "time_off:manage",
            "calendar:read",
            "monitoring:read",
            "monitoring:configure"
        ]);

        // Must NOT contain invalid monitoring keys
        codes.Should().NotContain("monitoring:view");
        codes.Should().NotContain("monitoring:view-settings");
        codes.Should().NotContain("integrations:read");
        codes.Should().Contain("integrations:manage");

        // settings:notifications was a duplicate of notifications:manage and is retired;
        // notifications:manage is the single canonical Phase 1 notification-management permission.
        codes.Should().NotContain("settings:notifications");
        codes.Should().Contain("notifications:manage");
        codes.Should().Contain("invitations:manage");
        codes.Should().Contain("employees:read:sensitive");
        codes.Should().Contain("employees:offboard");
    }
}
