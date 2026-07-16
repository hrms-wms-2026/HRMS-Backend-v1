using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Roles;

public class ArchiveRoleCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ArchiveRoleCommandHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new ArchiveRoleCommandHandler(_roles.Object, _currentUser.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_NonSystemRole_CallsRemove_AndPersists()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "Manager", IsSystem = false };
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        var sut = BuildSut();
        var result = await sut.Handle(new ArchiveRoleCommand(roleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _roles.Verify(r => r.Remove(role), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SystemRole_ReturnsForbidden_AndDoesNotRemove()
    {
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, TenantId = TenantId, Name = "SuperAdmin", IsSystem = true };
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        var sut = BuildSut();
        var result = await sut.Handle(new ArchiveRoleCommand(roleId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _roles.Verify(r => r.Remove(It.IsAny<Role>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var roleId = Guid.NewGuid();
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new ArchiveRoleCommand(roleId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
