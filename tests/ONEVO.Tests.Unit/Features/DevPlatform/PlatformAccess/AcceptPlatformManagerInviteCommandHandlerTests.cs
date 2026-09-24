using Moq;
using FluentAssertions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class AcceptPlatformManagerInviteCommandHandlerTests
{
    private readonly Mock<IPlatformUserInviteRepository> _invites = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformUserCredentialRepository> _credentials = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly DateTimeOffset _now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    private AcceptPlatformManagerInviteCommandHandler CreateHandler()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _tokens.Setup(t => t.HashToken("raw-token")).Returns("hashed-token");
        return new AcceptPlatformManagerInviteCommandHandler(
            _invites.Object, _users.Object, _credentials.Object, _authEvents.Object,
            _passwordHasher.Object, _tokens.Object, _clock.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsNotFound()
    {
        _invites.Setup(i => i.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserInvite?)null);

        var result = await CreateHandler().Handle(
            new AcceptPlatformManagerInviteCommand("raw-token", "NewPassword1!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(true, false, false, "already been accepted")]
    [InlineData(false, true, false, "been revoked")]
    [InlineData(false, false, true, "has expired")]
    public async Task Handle_UnusableInvite_ReturnsSpecificMessage(
        bool accepted, bool revoked, bool expired, string expectedFragment)
    {
        var invite = new PlatformUserInvite
        {
            Id = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            AcceptedAt = accepted ? _now.AddMinutes(-5) : null,
            RevokedAt = revoked ? _now.AddMinutes(-5) : null,
            ExpiresAt = expired ? _now.AddMinutes(-5) : _now.AddHours(1)
        };
        _invites.Setup(i => i.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateHandler().Handle(
            new AcceptPlatformManagerInviteCommand("raw-token", "NewPassword1!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain(expectedFragment);
    }

    [Fact]
    public async Task Handle_ValidInvite_ActivatesUserAndCreatesCredential()
    {
        var userId = Guid.NewGuid();
        var invite = new PlatformUserInvite
        {
            Id = Guid.NewGuid(),
            PlatformUserId = userId,
            ExpiresAt = _now.AddHours(1)
        };
        var user = new PlatformUser { Id = userId, Status = PlatformUser.StatusPending, InviteStatus = PlatformUser.InvitePending };

        _invites.Setup(i => i.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>())).ReturnsAsync(invite);
        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(p => p.Hash("NewPassword1!")).Returns("hashed-password");

        var result = await CreateHandler().Handle(
            new AcceptPlatformManagerInviteCommand("raw-token", "NewPassword1!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.Status.Should().Be(PlatformUser.StatusActive);
        user.InviteStatus.Should().Be(PlatformUser.InviteAccepted);
        invite.AcceptedAt.Should().Be(_now);
        _credentials.Verify(c => c.AddAsync(
            It.Is<PlatformUserCredential>(cred =>
                cred.PlatformUserId == userId && cred.PasswordHash == "hashed-password"),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
