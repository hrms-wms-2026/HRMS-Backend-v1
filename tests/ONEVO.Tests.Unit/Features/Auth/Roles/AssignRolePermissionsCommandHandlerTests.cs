using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth.Roles;

public class AssignRolePermissionsCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<ITenantPermissionCatalogService> _catalog = new();
    private readonly Mock<IUserRoleRepository> _userRoles = new();
    private readonly Mock<IPermissionVersionService> _permissionVersion = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static PermissionCatalogDto TestCatalog() =>
        new(
            [],
            [new PermissionCatalogItemDto(null, "inbox:read", "U", "inbox", false, true)]);

    private AssignRolePermissionsCommandHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _catalog.Setup(c => c.GetCatalogAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestCatalog());
        _clock.SetupGet(c => c.UtcNow).Returns(FixedNow);
        return new AssignRolePermissionsCommandHandler(
            _roles.Object,
            _rolePermissions.Object,
            _permissions.Object,
            _entitlements.Object,
            _catalog.Object,
            _userRoles.Object,
            _permissionVersion.Object,
            _currentUser.Object,
            _uow.Object,
            _clock.Object);
    }

    [Fact]
    public async Task Handle_ReplacesPermissionSet_AddsAndRemovesDelta()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        var permKept = new Permission { Id = Guid.NewGuid(), Code = "leave:read", Module = "leave" };
        var permNew = new Permission { Id = Guid.NewGuid(), Code = "leave:approve", Module = "leave" };
        var permOld = new Permission { Id = Guid.NewGuid(), Code = "leave:create", Module = "leave" };

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RolePermission { RoleId = roleId, PermissionId = permKept.Id },
                new RolePermission { RoleId = roleId, PermissionId = permOld.Id }
            });

        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permKept, permNew });
        _entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(
                TenantId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permKept, permNew });
        _userRoles.Setup(u => u.ListUserIdsByRoleAsync(roleId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        IEnumerable<RolePermission>? added = null;
        IEnumerable<RolePermission>? removed = null;
        _rolePermissions
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<RolePermission>, CancellationToken>((rps, _) => added = rps.ToList())
            .Returns(Task.CompletedTask);
        _rolePermissions
            .Setup(r => r.RemoveRange(It.IsAny<IEnumerable<RolePermission>>()))
            .Callback<IEnumerable<RolePermission>>(rps => removed = rps.ToList());

        var sut = BuildSut();
        var result = await sut.Handle(
            new AssignRolePermissionsCommand(roleId, new[] { permKept.Id, permNew.Id }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Select(rp => rp.PermissionId).Should().BeEquivalentTo(new[] { permNew.Id });
        removed.Should().NotBeNull();
        removed!.Select(rp => rp.PermissionId).Should().BeEquivalentTo(new[] { permOld.Id });
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _permissionVersion.Verify(
            v => v.IncrementVersionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyPermissionList_ClearsAllExisting()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        var permA = Guid.NewGuid();

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RolePermission { RoleId = roleId, PermissionId = permA } });
        _userRoles.Setup(u => u.ListUserIdsByRoleAsync(roleId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = BuildSut();
        var result = await sut.Handle(
            new AssignRolePermissionsCommand(roleId, Array.Empty<Guid>()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _rolePermissions.Verify(r => r.RemoveRange(It.Is<IEnumerable<RolePermission>>(
            rps => rps.Any(rp => rp.PermissionId == permA))), Times.Once);
        _rolePermissions.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _permissionVersion.Verify(
            v => v.IncrementVersionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownPermission_ReturnsFailure_AndDoesNotPersist()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        var unknownPerm = Guid.NewGuid();

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Permission>());

        var sut = BuildSut();
        var result = await sut.Handle(
            new AssignRolePermissionsCommand(roleId, new[] { unknownPerm }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unknown permission ids");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SystemRole_ReturnsForbidden()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "SuperAdmin", IsSystem = true };
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        var sut = BuildSut();
        var result = await sut.Handle(
            new AssignRolePermissionsCommand(roleId, Array.Empty<Guid>()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_IncrementsPermissionVersion_ForEachUserWithRole_WhenPermissionsChange()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        var permA = new Permission { Id = Guid.NewGuid(), Code = "leave:read", Module = "leave" };
        var permB = new Permission { Id = Guid.NewGuid(), Code = "leave:approve", Module = "leave" };
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RolePermission { RoleId = roleId, PermissionId = permA.Id }]);
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permA, permB });
        _entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(
                TenantId,
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permA, permB });
        _rolePermissions
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        _userRoles.Setup(u => u.ListUserIdsByRoleAsync(roleId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([user1, user2]);
        await sut.Handle(
            new AssignRolePermissionsCommand(roleId, new[] { permA.Id, permB.Id }),
            CancellationToken.None);

        _permissionVersion.Verify(v => v.IncrementVersionAsync(user1, It.IsAny<CancellationToken>()), Times.Once);
        _permissionVersion.Verify(v => v.IncrementVersionAsync(user2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PassesCurrentTimeFromDateTimeProvider_ToListUserIdsByRoleAsync()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        var permNew = new Permission { Id = Guid.NewGuid(), Code = "leave:approve", Module = "leave" };

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePermission>());
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permNew });
        _entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(
                TenantId,
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permNew });
        _rolePermissions
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        _userRoles.Setup(u => u.ListUserIdsByRoleAsync(roleId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await sut.Handle(
            new AssignRolePermissionsCommand(roleId, new[] { permNew.Id }),
            CancellationToken.None);

        _userRoles.Verify(
            u => u.ListUserIdsByRoleAsync(roleId, FixedNow, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OutOfPlanPermission_ReturnsFailure()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        var payroll = new Permission { Id = Guid.NewGuid(), Code = "payroll:run", Module = "payroll" };

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { payroll });
        _entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(
                TenantId,
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = BuildSut();
        var result = await sut.Handle(
            new AssignRolePermissionsCommand(roleId, new[] { payroll.Id }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("payroll:run");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
