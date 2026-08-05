using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.ResetAdminPassword;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ResetAdminPasswordCommandHandlerTests
{
    private const string GenericTokenError = "Invalid or expired reset token.";

    private sealed class HandlerFixture
    {
        public Mock<IPlatformUserRepository> PlatformUsers { get; } = new();
        public Mock<IPlatformUserCredentialRepository> Credentials { get; } = new();
        public Mock<IPlatformUserSessionRepository> Sessions { get; } = new();
        public Mock<IPlatformAuthEventRepository> AuthEvents { get; } = new();
        public Mock<IPasswordHasher> PasswordHasher { get; } = new();
        public Mock<ISecureTokenGenerator> Tokens { get; } = new();
        public Mock<IOutboxWriter> Outbox { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public HandlerFixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-08-04T12:00:00Z"));
            PasswordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns((string pw) => $"hashed-{pw}");
            Tokens.Setup(t => t.HashToken(It.IsAny<string>())).Returns((string raw) => $"hash-of-{raw}");
            UnitOfWork
                .Setup(u => u.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((op, ct) => op(ct));
        }

        public ResetAdminPasswordCommandHandler Build() => new(
            PlatformUsers.Object, Credentials.Object, Sessions.Object, AuthEvents.Object,
            PasswordHasher.Object, Tokens.Object, Outbox.Object, Clock.Object, UnitOfWork.Object);

        public void SetupConsumedToken(Guid? userId) =>
            Credentials
                .Setup(c => c.TryConsumeResetTokenAsync(
                    "hash-of-raw-token", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);
    }

    [Fact]
    public async Task Handle_ValidToken_UpdatesPasswordRevokesSessionsAndNotifies()
    {
        var fixture = new HandlerFixture();
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(), Email = "admin@onexso.test", FullName = "Test Admin",
            Status = PlatformUser.StatusActive
        };
        var credential = new PlatformUserCredential
        {
            Id = Guid.NewGuid(), PlatformUserId = user.Id,
            CredentialType = PlatformUserCredential.PasswordType, PasswordHash = "old-hash"
        };
        fixture.SetupConsumedToken(user.Id);
        fixture.Credentials.Setup(c => c.GetActivePasswordCredentialAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        fixture.PlatformUsers.Setup(p => p.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await fixture.Build().Handle(
            new ResetAdminPasswordCommand("raw-token", "NewPassphrase123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        credential.PasswordHash.Should().Be("hashed-NewPassphrase123");
        fixture.Sessions.Verify(s => s.RevokeAllByUserIdAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Outbox.Verify(o => o.EnqueueAsync(
            OutboxMessageTypes.AdminPasswordChangedEmail,
            It.Is<AdminPasswordChangedEmailPayload>(p => p.Email == user.Email),
            null, It.IsAny<CancellationToken>()), Times.Once);
        fixture.AuthEvents.Verify(a => a.AddAsync(
            It.Is<PlatformAuthEvent>(e => e.EventType == PlatformAuthEvent.AdminPasswordResetCompleted && e.UserId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidOrExpiredToken_ReturnsGenericError()
    {
        var fixture = new HandlerFixture();
        fixture.SetupConsumedToken(null);

        var result = await fixture.Build().Handle(
            new ResetAdminPasswordCommand("raw-token", "NewPassphrase123"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        fixture.Sessions.Verify(s => s.RevokeAllByUserIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoActiveCredentialAfterConsumption_BurnsTokenAndReturnsGenericError()
    {
        var fixture = new HandlerFixture();
        var userId = Guid.NewGuid();
        fixture.SetupConsumedToken(userId);
        fixture.Credentials.Setup(c => c.GetActivePasswordCredentialAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserCredential?)null);

        var result = await fixture.Build().Handle(
            new ResetAdminPasswordCommand("raw-token", "NewPassphrase123"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.PlatformUsers.Verify(p => p.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
