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
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ListRolesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new ListRolesQueryHandler(_roles.Object, _rolePermissions.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ReturnsAllRolesWithPermissionCounts()
    {
        var role1 = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Manager", IsSystem = false };
        var role2 = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Admin", IsSystem = true };

        _roles.Setup(r => r.ListByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { role1, role2 });

        _rolePermissions.Setup(r => r.ListByRoleAsync(role1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RolePermission { RoleId = role1.Id, PermissionId = Guid.NewGuid() },
                new RolePermission { RoleId = role1.Id, PermissionId = Guid.NewGuid() }
            });
        _rolePermissions.Setup(r => r.ListByRoleAsync(role2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePermission>());

        var sut = BuildSut();
        var result = await sut.Handle(new ListRolesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var summaries = result.Value!;
        summaries.Should().HaveCount(2);
        summaries.First(r => r.Id == role1.Id).PermissionCount.Should().Be(2);
        summaries.First(r => r.Id == role2.Id).PermissionCount.Should().Be(0);
        summaries.First(r => r.Id == role2.Id).IsSystem.Should().BeTrue();
    }
}
