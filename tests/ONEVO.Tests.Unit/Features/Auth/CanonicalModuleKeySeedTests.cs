using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.ModuleCatalog.Helpers;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Seeders write canonical module keys only (DEC-040, DEC-041, DEC-042, DEC-048), so permission
/// and ownership rows match the canonical keys the entitlement service returns.
/// </summary>
public class CanonicalModuleKeySeedTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext db)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var seeder = new PermissionSeeder(services.BuildServiceProvider(), NullLogger<PermissionSeeder>.Instance);
        var method = typeof(PermissionSeeder).GetMethod(
            "SeedPermissionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(seeder, [db, CancellationToken.None])!;
    }

    [Theory]
    [InlineData("leave:read", "time_off")]
    [InlineData("leave:manage", "time_off")]
    [InlineData("monitoring:read", "monitoring")]
    [InlineData("activity:read:self", "monitoring")]
    [InlineData("monitoring:configure", "monitoring")]
    [InlineData("monitoring:screenshots:request", "monitoring")]
    [InlineData("attendance:read", "time_attendance")]
    [InlineData("attendance:approve", "time_attendance")]
    [InlineData("attendance:write", "time_attendance")]
    [InlineData("projects:create", "work_management")]
    [InlineData("legal_entity:read", "org")]
    [InlineData("verification:view", "verification")]
    public async Task PermissionSeeder_UsesCanonicalModule(string code, string expectedModule)
    {
        using var db = BuildInMemoryDb();
        await SeedPermissionsAsync(db);

        (await db.Permissions.SingleAsync(p => p.Code == code)).Module.Should().Be(expectedModule);
    }

    [Fact]
    public async Task PermissionSeeder_WritesNoLegacyModuleKey()
    {
        using var db = BuildInMemoryDb();
        await SeedPermissionsAsync(db);

        var modules = await db.Permissions.Select(p => p.Module).Distinct().ToListAsync();
        modules.Should().NotIntersectWith(ModuleKeyAliases.LegacyToCanonical.Keys);
    }

    [Fact]
    public async Task ModuleCatalogSeeder_OwnsProjectsCreateUnderWorkManagement()
    {
        using var db = BuildInMemoryDb();
        db.Permissions.Add(new Permission { Id = Guid.NewGuid(), Code = "projects:create", Module = "work_management" });
        await db.SaveChangesAsync();

        await ModuleCatalogSeeder.SeedAsync(db, CancellationToken.None);

        (await db.ModulePermissionOwnerships.SingleAsync(o => o.PermissionCode == "projects:create"))
            .ModuleKey.Should().Be("work_management");
    }

    [Fact]
    public async Task ModuleCatalogSeeder_NewOrgFeatureIsOwnedByOrg()
    {
        using var db = BuildInMemoryDb();

        await ModuleCatalogSeeder.SeedAsync(db, CancellationToken.None);

        (await db.ModuleFeatures.SingleAsync(f => f.FeatureKey == "org.structure_management"))
            .ModuleKey.Should().Be("org");
    }

    [Fact]
    public async Task ModuleCatalogSeeder_EveryFeatureAndOwnershipModuleIsCanonical()
    {
        using var db = BuildInMemoryDb();
        await SeedPermissionsAsync(db);

        await ModuleCatalogSeeder.SeedAsync(db, CancellationToken.None);

        var featureModules = await db.ModuleFeatures.Select(f => f.ModuleKey).Distinct().ToListAsync();
        var ownershipModules = await db.ModulePermissionOwnerships.Select(o => o.ModuleKey).Distinct().ToListAsync();
        featureModules.Concat(ownershipModules).Should().NotIntersectWith(ModuleKeyAliases.LegacyToCanonical.Keys);
    }
}
