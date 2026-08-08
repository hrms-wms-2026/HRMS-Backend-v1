using Moq;
using FluentAssertions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class InvitePlatformManagerCommandHandlerTests
{
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformRoleRepository> _roles = new();
    private readonly Mock<IPlatformUserInviteRepository> _invites = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<ICurrentPlatformUserContext> _currentUser = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private InvitePlatformManagerCommandHandler CreateHandler()
    {
        _clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        return new InvitePlatformManagerCommandHandler(
            _users.Object, _roles.Object, _invites.Object, _authEvents.Object, _currentUser.Object,
            _tokens.Object, _outbox.Object, _clock.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_EmptyRoleIds_ReturnsFailure()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("new@example.com", "New Manager", []),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _users.Verify(u => u.AddAsync(It.IsAny<PlatformUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownRoleId_ReturnsNotFound()
    {
        var roleId = Guid.NewGuid();
        _users.Setup(u => u.GetByEmailAsync("new@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);
        _roles.Setup(r => r.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformRole?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("new@example.com", "New Manager", [roleId]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ExistingEmail_ReturnsConflict()
    {
        var roleId = Guid.NewGuid();
        _users.Setup(u => u.GetByEmailAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUser { Id = Guid.NewGuid(), Email = "existing@example.com" });
        _roles.Setup(r => r.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRole { Id = roleId, Name = "Manager" });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("existing@example.com", "New Manager", [roleId]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesUserRolesInviteAndOutboxMessage()
    {
        var roleId = Guid.NewGuid();
        _users.Setup(u => u.GetByEmailAsync("new@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);
        _roles.Setup(r => r.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRole { Id = roleId, Name = "Manager" });
        _tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-token-value");
        _tokens.Setup(t => t.HashToken("raw-token-value")).Returns("hashed-token-value");

        PlatformUser? capturedUser = null;
        _users.Setup(u => u.AddAsync(It.IsAny<PlatformUser>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformUser, CancellationToken>((u, _) => capturedUser = u)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("new@example.com", "New Manager", [roleId]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedUser.Should().NotBeNull();
        capturedUser!.Status.Should().Be(PlatformUser.StatusPending);
        capturedUser.Email.Should().Be("new@example.com");

        _invites.Verify(i => i.AddAsync(
            It.Is<PlatformUserInvite>(inv =>
                inv.InviteTokenHash == "hashed-token-value" &&
                inv.Email == "new@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);

        _outbox.Verify(o => o.EnqueueAsync(
            OutboxMessageTypes.PlatformManagerInviteEmail,
            It.Is<PlatformManagerInviteEmailPayload>(p =>
                p.Email == "new@example.com" && p.RawToken == "raw-token-value"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
