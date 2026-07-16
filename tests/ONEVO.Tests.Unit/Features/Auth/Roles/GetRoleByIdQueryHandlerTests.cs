using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Features.Auth.Roles.Queries.GetRoleById;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth.Roles;

public class GetRoleByIdQueryHandlerTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<ITenantPermissionCatalogService> _catalog = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PermissionCatalogDto TestCatalog() =>
        new(
            [],
            [new PermissionCatalogItemDto(null, "inbox:read", "U", "inbox", false, true)]);

    private GetRoleByIdQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _catalog.Setup(c => c.GetCatalogAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestCatalog());
        return new GetRoleByIdQueryHandler(
            _roles.Object,
            _rolePermissions.Object,
            _permissions.Object,
            _catalog.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ReturnsRoleWithExpandedPermissions()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", Description = "x", IsSystem = false };
        var permA = new Permission { Id = Guid.NewGuid(), Code = "leave:read", Description = "View leave", Module = "leave" };

        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RolePermission { RoleId = roleId, PermissionId = permA.Id } });
        _permissions.Setup(p => p.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { permA });

        var sut = BuildSut();
        var result = await sut.Handle(new GetRoleByIdQuery(roleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(roleId);
        result.Value.Permissions.Should().ContainSingle()
            .Which.Code.Should().Be("leave:read");
        result.Value.UniversalPermissions.Should().ContainSingle().Which.Code.Should().Be("inbox:read");
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFound()
    {
        var roleId = Guid.NewGuid();
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new GetRoleByIdQuery(roleId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
