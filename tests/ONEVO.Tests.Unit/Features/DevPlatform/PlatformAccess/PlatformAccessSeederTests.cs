using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class PlatformAccessSeederTests
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
    public async Task SeedAsync_SeedsFullPermissionCatalog()
    {
        using var db = BuildInMemoryDb();

        await PlatformAccessSeeder.SeedAsync(db, null, null, CancellationToken.None);

        var seededCodes = await db.PlatformPermissions.Select(p => p.Code).ToListAsync();
        var expectedCodes = PlatformPermissionCatalog.GetAll().Select(d => d.Code).ToList();

        seededCodes.Should().BeEquivalentTo(expectedCodes);
        seededCodes.Should().Contain(new[]
        {
            "platform.tenants.read",
            "platform.module_catalog.read",
            "platform.accounts.read",
            "platform.roles.read",
            "platform.security.read",
            "platform.audit.read",
            "platform.system_config.read",
            "platform.app_catalog.read"
        });
    }

    [Fact]
    public async Task SeedAsync_SeedsDocumentedSystemRoles()
    {
        using var db = BuildInMemoryDb();

        await PlatformAccessSeeder.SeedAsync(db, null, null, CancellationToken.None);

        var roles = await db.PlatformRoles.ToListAsync();
        roles.Should().HaveCount(PlatformRoleCatalog.GetSystemRoles().Count);
        roles.Should().OnlyContain(r => r.IsSystem && r.IsActive);
        roles.Select(r => r.Name).Should().BeEquivalentTo(new[]
        {
            "Platform Super Admin",
            "Tenant Operations Manager",
            "Billing Manager",
            "Security Auditor",
            "Module Catalog Manager",
            "Operations Engineer",
            "Read-Only Viewer"
        });
    }

    [Fact]
    public async Task SeedAsync_GrantsAllPermissionsToSuperAdmin_AndOnlySuperAdmin()
    {
        using var db = BuildInMemoryDb();

        await PlatformAccessSeeder.SeedAsync(db, null, null, CancellationToken.None);

        var superAdmin = await db.PlatformRoles.SingleAsync(r => r.Name == PlatformRoleCatalog.SuperAdmin);
        var superAdminGrants = await db.PlatformRolePermissions
            .Where(rp => rp.RoleId == superAdmin.Id)
            .Select(rp => rp.PermissionCode)
            .ToListAsync();

        superAdminGrants.Should().BeEquivalentTo(
            PlatformPermissionCatalog.GetAll().Select(d => d.Code));

        // Other seeded roles have no grants until the docs define exact mappings
        // (PLATFORM_ROLE_PERMISSION_GAPS.md).
        var otherGrants = await db.PlatformRolePermissions
            .Where(rp => rp.RoleId != superAdmin.Id)
            .CountAsync();
        otherGrants.Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotentAndDeterministic()
    {
        using var db = BuildInMemoryDb();

        await PlatformAccessSeeder.SeedAsync(db, "boot@onevo.io", "Boot Admin", CancellationToken.None);
        var permissionsAfterFirst = await db.PlatformPermissions.CountAsync();
        var rolesAfterFirst = await db.PlatformRoles.CountAsync();
        var grantsAfterFirst = await db.PlatformRolePermissions.CountAsync();
        var usersAfterFirst = await db.PlatformUsers.CountAsync();
        var userRolesAfterFirst = await db.PlatformUserRoles.CountAsync();

        await PlatformAccessSeeder.SeedAsync(db, "boot@onevo.io", "Boot Admin", CancellationToken.None);

        (await db.PlatformPermissions.CountAsync()).Should().Be(permissionsAfterFirst);
        (await db.PlatformRoles.CountAsync()).Should().Be(rolesAfterFirst);
        (await db.PlatformRolePermissions.CountAsync()).Should().Be(grantsAfterFirst);
        (await db.PlatformUsers.CountAsync()).Should().Be(usersAfterFirst);
        (await db.PlatformUserRoles.CountAsync()).Should().Be(userRolesAfterFirst);
    }

    [Fact]
    public async Task SeedAsync_BootstrapsSuperAdminUser_FromConfigValues()
    {
        using var db = BuildInMemoryDb();

        await PlatformAccessSeeder.SeedAsync(db, "Boot@Onevo.io", "Boot Admin", CancellationToken.None);

        var user = await db.PlatformUsers.SingleAsync();
        user.Email.Should().Be("boot@onevo.io"); // normalized
        user.Status.Should().Be("active");

        var superAdmin = await db.PlatformRoles.SingleAsync(r => r.Name == PlatformRoleCatalog.SuperAdmin);
        var assignment = await db.PlatformUserRoles.SingleAsync();
        assignment.UserId.Should().Be(user.Id);
        assignment.RoleId.Should().Be(superAdmin.Id);
    }

    [Fact]
    public async Task SeedAsync_WithoutBootstrapConfig_CreatesNoUsers()
    {
        using var db = BuildInMemoryDb();

        await PlatformAccessSeeder.SeedAsync(db, null, null, CancellationToken.None);

        (await db.PlatformUsers.CountAsync()).Should().Be(0);
        (await db.PlatformUserRoles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_DevelopmentBootstrap_CreatesHashedCredentialOnlyWhenMissing()
    {
        using var db = BuildInMemoryDb();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(value => value.Hash("bootstrap-secret")).Returns("hashed-value");

        await PlatformAccessSeeder.SeedAsync(
            db,
            "boot@onevo.io",
            "Boot Admin",
            "bootstrap-secret",
            true,
            hasher.Object,
            CancellationToken.None);
        await PlatformAccessSeeder.SeedAsync(
            db,
            "boot@onevo.io",
            "Boot Admin",
            "changed-secret",
            true,
            hasher.Object,
            CancellationToken.None);

        var credential = await db.PlatformUserCredentials.SingleAsync();
        credential.PasswordHash.Should().Be("hashed-value");
        credential.PasswordHash.Should().NotBe("bootstrap-secret");
        hasher.Verify(value => value.Hash("bootstrap-secret"), Times.Once);
        hasher.Verify(value => value.Hash("changed-secret"), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_ProductionMode_DoesNotCreatePasswordCredential()
    {
        using var db = BuildInMemoryDb();
        var hasher = new Mock<IPasswordHasher>();

        await PlatformAccessSeeder.SeedAsync(
            db,
            "boot@onevo.io",
            "Boot Admin",
            "bootstrap-secret",
            false,
            hasher.Object,
            CancellationToken.None);

        (await db.PlatformUsers.CountAsync()).Should().Be(1);
        (await db.PlatformUserCredentials.CountAsync()).Should().Be(0);
        hasher.Verify(value => value.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_DevelopmentBootstrap_WithoutPasswordOrCredential_FailsClearly()
    {
        using var db = BuildInMemoryDb();
        var hasher = new Mock<IPasswordHasher>();

        var action = () => PlatformAccessSeeder.SeedAsync(
            db,
            "boot@onevo.io",
            "Boot Admin",
            null,
            true,
            hasher.Object,
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*DevAdmin__Password*platform_user_credentials*");
        (await db.PlatformUserCredentials.CountAsync()).Should().Be(0);
        hasher.Verify(value => value.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_DevelopmentBootstrap_WithoutPassword_ContinuesWhenCredentialExists()
    {
        using var db = BuildInMemoryDb();
        var hasher = new Mock<IPasswordHasher>();

        await PlatformAccessSeeder.SeedAsync(
            db,
            "boot@onevo.io",
            "Boot Admin",
            "initial-bootstrap-secret",
            true,
            new ONEVO.Infrastructure.Identity.Passwords.BCryptPasswordHasher(),
            CancellationToken.None);

        var originalHash = (await db.PlatformUserCredentials.SingleAsync()).PasswordHash;

        await PlatformAccessSeeder.SeedAsync(
            db,
            "boot@onevo.io",
            "Boot Admin",
            null,
            true,
            hasher.Object,
            CancellationToken.None);

        (await db.PlatformUserCredentials.SingleAsync()).PasswordHash.Should().Be(originalHash);
        hasher.Verify(value => value.Hash(It.IsAny<string>()), Times.Never);
    }
}
