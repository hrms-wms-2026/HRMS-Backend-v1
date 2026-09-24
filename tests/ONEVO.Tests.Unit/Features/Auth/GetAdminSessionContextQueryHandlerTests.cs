using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Queries.GetAdminSessionContext;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class GetAdminSessionContextQueryHandlerTests
{
    private readonly Mock<ICurrentPlatformUserContext> _currentUser = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformPermissionResolver> _resolver = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly DateTimeOffset _now = new(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);

    public GetAdminSessionContextQueryHandlerTests()
    {
        _clock.Setup(value => value.UtcNow).Returns(_now);
    }

    [Fact]
    public async Task Handle_AuthenticatedActiveUser_ReturnsSessionResponse()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@onevo.io",
            FullName = "Platform Admin",
            Status = PlatformUser.StatusActive
        };
        _currentUser.Setup(value => value.UserId).Returns(user.Id);
        _users.Setup(value => value.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _resolver.Setup(value => value.ResolveActiveUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformAccessProfile
            {
                UserId = user.Id,
                Email = user.Email,
                Status = user.Status,
                RoleNames = { "Platform Super Admin" },
                PermissionCodes = { "platform.accounts.read", "platform.accounts.manage" }
            });

        var result = await Handler().Handle(new GetAdminSessionContextQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PlatformUserId.Should().Be(user.Id);
        result.Value!.Email.Should().Be(user.Email);
        result.Value!.PlatformRole.Should().Be("Platform Super Admin");
        result.Value!.MfaRequired.Should().BeFalse();
        result.Value!.ExpiresAt.Should().BeAfter(_now);
        result.Value!.Permissions.Should().BeEquivalentTo(
            new[] { "platform.accounts.read", "platform.accounts.manage" });
    }

    [Fact]
    public async Task Handle_NoCurrentUserId_ReturnsUnauthorized()
    {
        _currentUser.Setup(value => value.UserId).Returns((Guid?)null);

        var result = await Handler().Handle(new GetAdminSessionContextQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        _users.Verify(value => value.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserNoLongerExists_ReturnsUnauthorized()
    {
        var userId = Guid.NewGuid();
        _currentUser.Setup(value => value.UserId).Returns(userId);
        _users.Setup(value => value.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var result = await Handler().Handle(new GetAdminSessionContextQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Handle_UserNoLongerActive_ReturnsForbidden()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@onevo.io",
            FullName = "Platform Admin",
            Status = PlatformUser.StatusActive
        };
        _currentUser.Setup(value => value.UserId).Returns(user.Id);
        _users.Setup(value => value.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _resolver.Setup(value => value.ResolveActiveUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformAccessProfile?)null);

        var result = await Handler().Handle(new GetAdminSessionContextQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    private GetAdminSessionContextQueryHandler Handler()
    {
        return new GetAdminSessionContextQueryHandler(
            _currentUser.Object,
            _users.Object,
            _resolver.Object,
            _clock.Object);
    }
}
