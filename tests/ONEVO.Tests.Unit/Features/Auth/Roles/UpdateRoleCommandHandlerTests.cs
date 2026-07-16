using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Roles;

public class UpdateRoleCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private UpdateRoleCommandHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new UpdateRoleCommandHandler(_roles.Object, _rolePermissions.Object, _currentUser.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_KnownRole_UpdatesNameAndDescription()
    {
        var roleId = Guid.NewGuid();
        var role = new Role
        {
            Id = roleId,
            TenantId = TenantId,
            Name = "Old",
            Description = "Old desc",
            IsSystem = false
        };
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, "New", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);
        _rolePermissions.Setup(r => r.ListByRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePermission>());

        var sut = BuildSut();
        var result = await sut.Handle(new UpdateRoleCommand(roleId, "New", "New desc"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New");
        result.Value.Description.Should().Be("New desc");
        role.Name.Should().Be("New");
        role.Description.Should().Be("New desc");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SystemRole_ReturnsForbidden_AndDoesNotPersist()
    {
        var roleId = Guid.NewGuid();
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = roleId, TenantId = TenantId, Name = "Admin", IsSystem = true });

        var sut = BuildSut();
        var result = await sut.Handle(new UpdateRoleCommand(roleId, "Renamed", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFound()
    {
        var roleId = Guid.NewGuid();
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new UpdateRoleCommand(roleId, "Anything", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_NameClashWithDifferentRole_ReturnsConflict()
    {
        var roleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = roleId, TenantId = TenantId, Name = "Old", IsSystem = false });
        _roles.Setup(r => r.GetByNameForTenantAsync(TenantId, "Manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = otherRoleId, TenantId = TenantId, Name = "Manager", IsSystem = false });

        var sut = BuildSut();
        var result = await sut.Handle(new UpdateRoleCommand(roleId, "Manager", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
