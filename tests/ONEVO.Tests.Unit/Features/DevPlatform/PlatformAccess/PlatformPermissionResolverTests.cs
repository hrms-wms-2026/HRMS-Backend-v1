using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Services.DevPlatform.PlatformAccess;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class PlatformPermissionResolverTests
{
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformAccessReadRepository> _access = new();

    private PlatformPermissionResolver BuildSut() => new(_users.Object, _access.Object);

    [Fact]
    public async Task ResolveActiveUser_UnknownUser_ReturnsNull()
    {
        _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((PlatformUser?)null);

        var profile = await BuildSut().ResolveActiveUserAsync(Guid.NewGuid());

        profile.Should().BeNull();
    }

    [Theory]
    [InlineData(PlatformUser.StatusPending)]
    [InlineData(PlatformUser.StatusInactive)]
    public async Task ResolveActiveUser_NonActiveStatus_ReturnsNull(string status)
    {
        var user = new PlatformUser { Id = Guid.NewGuid(), Email = "a@b.c", Status = status };
        _users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var profile = await BuildSut().ResolveActiveUserAsync(user.Id);

        profile.Should().BeNull();
    }

    [Fact]
    public async Task ResolveActiveUser_UnionsPermissionsAcrossActiveRoles_AndSkipsInactiveRoles()
    {
        var user = new PlatformUser { Id = Guid.NewGuid(), Email = "a@b.c", Status = PlatformUser.StatusActive };
        var activeRole = new PlatformRole { Id = Guid.NewGuid(), Name = "Role A", IsActive = true };
        var secondActiveRole = new PlatformRole { Id = Guid.NewGuid(), Name = "Role B", IsActive = true };
        var inactiveRole = new PlatformRole { Id = Guid.NewGuid(), Name = "Archived", IsActive = false };

        _users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _access.Setup(a => a.GetUserRolesAsync(user.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<PlatformUserRole>
               {
                   new() { UserId = user.Id, RoleId = activeRole.Id },
                   new() { UserId = user.Id, RoleId = secondActiveRole.Id },
                   new() { UserId = user.Id, RoleId = inactiveRole.Id },
               });
        _access.Setup(a => a.GetRolesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<PlatformRole> { activeRole, secondActiveRole, inactiveRole });
        _access.Setup(a => a.GetRolePermissionsAsync(
                   It.Is<IReadOnlyCollection<Guid>>(ids =>
                       ids.Contains(activeRole.Id) && ids.Contains(secondActiveRole.Id) && !ids.Contains(inactiveRole.Id)),
                   It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<PlatformRolePermission>
               {
                   new() { RoleId = activeRole.Id, PermissionCode = "platform.tenants.read" },
                   new() { RoleId = activeRole.Id, PermissionCode = "platform.tenants.manage" },
                   new() { RoleId = secondActiveRole.Id, PermissionCode = "platform.tenants.read" }, // duplicate
                   new() { RoleId = secondActiveRole.Id, PermissionCode = "platform.audit.read" },
               });

        var profile = await BuildSut().ResolveActiveUserAsync(user.Id);

        profile.Should().NotBeNull();
        profile!.RoleNames.Should().BeEquivalentTo("Role A", "Role B");
        profile.PermissionCodes.Should().BeEquivalentTo(new[]
        {
            "platform.tenants.read",
            "platform.tenants.manage",
            "platform.audit.read"
        });
    }

    [Fact]
    public async Task ResolveActiveUser_NoRoles_ReturnsEmptyPermissionSet()
    {
        var user = new PlatformUser { Id = Guid.NewGuid(), Email = "a@b.c", Status = PlatformUser.StatusActive };
        _users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _access.Setup(a => a.GetUserRolesAsync(user.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<PlatformUserRole>());

        var profile = await BuildSut().ResolveActiveUserAsync(user.Id);

        profile.Should().NotBeNull();
        profile!.PermissionCodes.Should().BeEmpty();
        profile.RoleNames.Should().BeEmpty();
    }
}
