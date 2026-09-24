using Moq;
using FluentAssertions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class RevokePlatformUserInviteCommandHandlerTests
{
    private readonly Mock<IPlatformUserInviteRepository> _invites = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private RevokePlatformUserInviteCommandHandler CreateHandler()
    {
        _clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        return new RevokePlatformUserInviteCommandHandler(_invites.Object, _users.Object, _clock.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokePlatformUserInviteCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ValidUser_RevokesInviteAndDeactivatesUser()
    {
        var userId = Guid.NewGuid();
        var invite = new PlatformUserInvite { Id = Guid.NewGuid(), PlatformUserId = userId };
        var user = new PlatformUser { Id = userId, Status = PlatformUser.StatusPending, InviteStatus = PlatformUser.InvitePending };

        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _invites.Setup(i => i.GetByPlatformUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(invite);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokePlatformUserInviteCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invite.RevokedAt.Should().NotBeNull();
        user.Status.Should().Be(PlatformUser.StatusInactive);
        user.InviteStatus.Should().Be(PlatformUser.InviteRevoked);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
