using FluentAssertions;
using Moq;

using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUsers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public sealed class ListPlatformUsersQueryHandlerTests
{
    private readonly Mock<IPlatformUserRepository> _users = new();

    [Fact]
    public async Task Handle_UsersWithAndWithoutRoles_MapsRoleOrEmptyString()
    {
        var withRole = new PlatformUser { Id = Guid.NewGuid(), Email = "a@onevo.io", FullName = "A", Status = PlatformUser.StatusActive };
        var withoutRole = new PlatformUser { Id = Guid.NewGuid(), Email = "b@onevo.io", FullName = "B", Status = PlatformUser.StatusActive };

        _users.Setup(r => r.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformUser> { withRole, withoutRole });
        _users.Setup(r => r.GetFirstRoleNamesByUserIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Contains(withRole.Id) && ids.Contains(withoutRole.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [withRole.Id] = "Platform Manager" });

        var handler = new ListPlatformUsersQueryHandler(_users.Object);

        var result = await handler.Handle(new ListPlatformUsersQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Single(u => u.Id == withRole.Id).Role.Should().Be("Platform Manager");
        result.Single(u => u.Id == withoutRole.Id).Role.Should().Be(string.Empty);
    }

    [Fact]
    public async Task Handle_NoUsers_DoesNotCallRoleLookup()
    {
        _users.Setup(r => r.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformUser>());

        var handler = new ListPlatformUsersQueryHandler(_users.Object);

        var result = await handler.Handle(new ListPlatformUsersQuery(), CancellationToken.None);

        result.Should().BeEmpty();
        _users.Verify(
            r => r.GetFirstRoleNamesByUserIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
