using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.Queries.ListRoles;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Roles;

public class ListRolesQueryHandlerTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ListRolesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _entitlements.Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["leave"]);
        return new ListRolesQueryHandler(
            _roles.Object,
            _rolePermissions.Object,
            _permissions.Object,
            _entitlements.Object,
            _currentUser.Object);
    }

    private static Permission Perm(string code, string module) =>
        new() { Id = Guid.NewGuid(), Code = code, Description = code, Module = module };

    [Fact]
    public async Task Handle_ReturnsAllRolesWithPermissionCounts()
    {
        var role1 = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Manager", IsSystem = false };
        var role2 = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Admin", IsSystem = true };
        var leaveRead = Perm("leave:read", "leave");
        var leaveManage = Perm("leave:manage", "leave");

        _roles.Setup(r => r.ListByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { role1, role2 });

        _rolePermissions.Setup(r => r.ListByRoleAsync(role1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RolePermission { RoleId = role1.Id, PermissionId = leaveRead.Id },
                new RolePermission { RoleId = role1.Id, PermissionId = leaveManage.Id }
            });
        _rolePermissions.Setup(r => r.ListByRoleAsync(role2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePermission>());
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { leaveRead, leaveManage });

        var sut = BuildSut();
        var result = await sut.Handle(new ListRolesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var summaries = result.Value!;
        summaries.Should().HaveCount(2);
        summaries.First(r => r.Id == role1.Id).PermissionCount.Should().Be(2);
        summaries.First(r => r.Id == role2.Id).PermissionCount.Should().Be(0);
        summaries.First(r => r.Id == role2.Id).IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CountsOnlyPermissionsTheRoleGrants()
    {
        // Mirrors PermissionResolver: subscribed modules + platform baseline modules count;
        // unsubscribed modules and the "*" wildcard do not.
        var owner = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Owner", IsSystem = true };
        var perms = new[]
        {
            Perm("leave:read", "leave"),
            Perm("roles:read", "roles"),
            Perm("payroll:read", "payroll"),
            Perm("*", "leave"),
            Perm("activity:read:self", "leave") // module auto-grant: everyone gets it, not the role
        };

        _roles.Setup(r => r.ListByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { owner });
        _rolePermissions.Setup(r => r.ListByRoleAsync(owner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(perms.Select(p => new RolePermission { RoleId = owner.Id, PermissionId = p.Id }).ToArray());
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(perms);

        var sut = BuildSut();
        var result = await sut.Handle(new ListRolesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Single().PermissionCount.Should().Be(2);
    }
}
