using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.ResetPlatformUserMfa;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

/// <summary>
/// F-02 recovery (decision D2): another admin resets a lost authenticator, never their own.
/// </summary>
public sealed class ResetPlatformUserMfaCommandHandlerTests
{
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformUserSessionRepository> _sessions = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<ICurrentPlatformUserContext> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<PlatformAuthEvent> _writtenEvents = [];
    private readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _callerId = Guid.NewGuid();

    public ResetPlatformUserMfaCommandHandlerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _currentUser.Setup(c => c.UserId).Returns(_callerId);
        _authEvents
            .Setup(a => a.AddAsync(It.IsAny<PlatformAuthEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformAuthEvent, CancellationToken>((e, _) => _writtenEvents.Add(e))
            .Returns(Task.CompletedTask);
    }

    private ResetPlatformUserMfaCommandHandler Handler() =>
        new(_users.Object, _sessions.Object, _authEvents.Object, _currentUser.Object, _clock.Object, _uow.Object);

    private PlatformUser EnrolledTarget()
    {
        var target = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "lost-device@onevo.io",
            Status = PlatformUser.StatusActive,
            MfaStatus = PlatformUser.MfaEnrolled,
            MfaSecret = "encrypted-totp-secret"
        };
        _users.Setup(u => u.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        return target;
    }

    [Fact]
    public async Task Handle_ResetsAnotherAdmin_ClearsSecret_RevokesSessions_AndAudits()
    {
        var target = EnrolledTarget();

        var result = await Handler().Handle(new ResetPlatformUserMfaCommand(target.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        target.MfaSecret.Should().BeNull();
        target.MfaStatus.Should().Be(PlatformUser.MfaNotEnrolled);
        _users.Verify(u => u.UpdateUser(target), Times.Once);
        _sessions.Verify(s => s.RevokeAllByUserIdAsync(target.Id, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var authEvent = _writtenEvents.Should().ContainSingle().Which;
        authEvent.EventType.Should().Be(PlatformAuthEvent.MfaReset);
        authEvent.UserId.Should().Be(target.Id);
        authEvent.CreatedAt.Should().Be(_now);
        authEvent.MetadataJson.Should().Contain(_callerId.ToString());
        authEvent.MetadataJson.Should().NotContain("encrypted-totp-secret");
    }

    [Fact]
    public async Task Handle_SelfReset_IsRefused_AndChangesNothing()
    {
        var self = EnrolledTarget();
        _currentUser.Setup(c => c.UserId).Returns(self.Id);

        var result = await Handler().Handle(new ResetPlatformUserMfaCommand(self.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        self.MfaSecret.Should().Be("encrypted-totp-secret");
        self.MfaStatus.Should().Be(PlatformUser.MfaEnrolled);
        _sessions.Verify(s => s.RevokeAllByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _writtenEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownTarget_ReturnsNotFound()
    {
        var unknown = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync((PlatformUser?)null);

        var result = await Handler().Handle(new ResetPlatformUserMfaCommand(unknown), CancellationToken.None);

        result.StatusCode.Should().Be(404);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoAuthenticatedCaller_ReturnsUnauthorized()
    {
        var target = EnrolledTarget();
        _currentUser.Setup(c => c.UserId).Returns((Guid?)null);

        var result = await Handler().Handle(new ResetPlatformUserMfaCommand(target.Id), CancellationToken.None);

        result.StatusCode.Should().Be(401);
        target.MfaStatus.Should().Be(PlatformUser.MfaEnrolled);
    }
}
