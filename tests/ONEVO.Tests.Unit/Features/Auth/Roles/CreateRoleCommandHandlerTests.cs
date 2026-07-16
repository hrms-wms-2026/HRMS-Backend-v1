using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.Commands.CreateRole;
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

public class CreateRoleCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<ITenantPermissionCatalogService> _catalog = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PermissionCatalogDto TestCatalog() =>
        new(
            [],
            [new PermissionCatalogItemDto(null, "inbox:read", "U", "inbox", false, true)]);

    private CreateRoleCommandHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _catalog.Setup(c => c.GetCatalogAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestCatalog());
        return new CreateRoleCommandHandler(
            _roles.Object,
            _rolePermissions.Object,
            _permissions.Object,
            _entitlements.Object,
            _catalog.Object,
            _currentUser.Object,
            _uow.Object);
    }

    [Fact]
    public async Task Handle_NewRole_NoPermissions_ReturnsSuccessAndPersists()
    {
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, "Manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateRoleCommand("Manager", "Team manager", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Manager");
        result.Value.Description.Should().Be("Team manager");
        result.Value.IsSystem.Should().BeFalse();
        result.Value.Permissions.Should().BeEmpty();
        result.Value.UniversalPermissions.Should().ContainSingle().Which.Code.Should().Be("inbox:read");

        _roles.Verify(r => r.AddAsync(It.Is<Role>(role => role.TenantId == TenantId && role.Name == "Manager"),
            It.IsAny<CancellationToken>()), Times.Once);
        _rolePermissions.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NameAlreadyExists_ReturnsConflict_AndDoesNotPersist()
    {
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, "Manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Manager" });

        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateRoleCommand("Manager", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already exists");

        _roles.Verify(r => r.AddAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownPermissionId_ReturnsFailure_AndDoesNotPersist()
    {
        var unknownId = Guid.NewGuid();
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Permission>());

        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateRoleCommand("Manager", null, new[] { unknownId }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unknown permission ids");
        result.Error.Should().Contain(unknownId.ToString());

        _roles.Verify(r => r.AddAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        _catalog.Setup(c => c.GetCatalogAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestCatalog());
        var sut = new CreateRoleCommandHandler(
            _roles.Object, _rolePermissions.Object, _permissions.Object,
            _entitlements.Object, _catalog.Object, _currentUser.Object, _uow.Object);

        var result = await sut.Handle(
            new CreateRoleCommand("Manager", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_PersistsAllRequestedPermissions()
    {
        var permA = new Permission { Id = Guid.NewGuid(), Code = "leave:read", Description = "", Module = "leave" };
        var permB = new Permission { Id = Guid.NewGuid(), Code = "leave:approve", Description = "", Module = "leave" };
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permA, permB });
        _entitlements
            .Setup(e => e.GetAssignablePermissionsForTenantAsync(
                TenantId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permA, permB });

        IEnumerable<RolePermission>? captured = null;
        _rolePermissions
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<RolePermission>, CancellationToken>((rps, _) => captured = rps.ToList())
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.Handle(
            new CreateRoleCommand("Leave Approver", null, new[] { permA.Id, permB.Id }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Permissions.Should().HaveCount(2);
        captured.Should().NotBeNull();
        captured!.Select(rp => rp.PermissionId).Should().BeEquivalentTo(new[] { permA.Id, permB.Id });
    }

    [Fact]
    public async Task Handle_PermissionOutsideTenantModules_ReturnsFailure()
    {
        var payroll = new Permission { Id = Guid.NewGuid(), Code = "payroll:run", Description = "", Module = "payroll" };
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);
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
            new CreateRoleCommand("Payroll", null, new[] { payroll.Id }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("outside this tenant's active modules");
        result.Error.Should().Contain("payroll:run");
        _roles.Verify(r => r.AddAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
