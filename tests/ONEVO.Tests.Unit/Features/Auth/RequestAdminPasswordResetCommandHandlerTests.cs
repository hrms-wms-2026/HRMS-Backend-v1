using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.RequestAdminPasswordReset;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class RequestAdminPasswordResetCommandHandlerTests
{
    private sealed class HandlerFixture
    {
        public Mock<IPlatformUserRepository> PlatformUsers { get; } = new();
        public Mock<IPlatformUserCredentialRepository> Credentials { get; } = new();
        public Mock<IPlatformAuthEventRepository> AuthEvents { get; } = new();
        public Mock<ISecureTokenGenerator> Tokens { get; } = new();
        public Mock<IOutboxWriter> Outbox { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public HandlerFixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-08-04T12:00:00Z"));
            Tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-token");
            Tokens.Setup(t => t.HashToken("raw-token")).Returns("hashed-raw-token");
        }

        public RequestAdminPasswordResetCommandHandler Build() => new(
            PlatformUsers.Object, Credentials.Object, AuthEvents.Object,
            Tokens.Object, Outbox.Object, Clock.Object, UnitOfWork.Object);
    }

    private static PlatformUser BuildActiveUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin@onexso.test",
        FullName = "Test Admin",
        Status = PlatformUser.StatusActive
    };

    [Fact]
    public async Task Handle_KnownActiveUser_SetsTokenAndEnqueuesEmail()
    {
        var fixture = new HandlerFixture();
        var user = BuildActiveUser();
        var credential = new PlatformUserCredential
        {
            Id = Guid.NewGuid(), PlatformUserId = user.Id,
            CredentialType = PlatformUserCredential.PasswordType, PasswordHash = "hash"
        };
        fixture.PlatformUsers.Setup(p => p.GetByEmailAsync("admin@onexso.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        fixture.Credentials.Setup(c => c.GetActivePasswordCredentialAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);

        var result = await fixture.Build().Handle(
            new RequestAdminPasswordResetCommand("Admin@Onexso.test", "1.2.3.4", "test-agent"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        credential.ResetTokenHash.Should().Be("hashed-raw-token");
        credential.ResetTokenExpiresAt.Should().Be(DateTimeOffset.Parse("2026-08-04T13:00:00Z"));
        fixture.Outbox.Verify(o => o.EnqueueAsync(
            OutboxMessageTypes.AdminPasswordResetEmail,
            It.Is<AdminPasswordResetEmailPayload>(p => p.Email == user.Email && p.RawToken == "raw-token"),
            null, It.IsAny<CancellationToken>()), Times.Once);
        fixture.AuthEvents.Verify(a => a.AddAsync(
            It.Is<PlatformAuthEvent>(e => e.EventType == PlatformAuthEvent.AdminPasswordResetRequested && e.UserId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownEmail_ReturnsGenericSuccessWithoutSideEffects()
    {
        var fixture = new HandlerFixture();
        fixture.PlatformUsers.Setup(p => p.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var result = await fixture.Build().Handle(
            new RequestAdminPasswordResetCommand("nobody@onexso.test", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Outbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsGenericSuccessWithoutSideEffects()
    {
        var fixture = new HandlerFixture();
        var user = BuildActiveUser();
        user.Status = PlatformUser.StatusInactive;
        fixture.PlatformUsers.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await fixture.Build().Handle(
            new RequestAdminPasswordResetCommand(user.Email, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Credentials.Verify(c => c.GetActivePasswordCredentialAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoActiveCredential_ReturnsGenericSuccessWithoutSideEffects()
    {
        var fixture = new HandlerFixture();
        var user = BuildActiveUser();
        fixture.PlatformUsers.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        fixture.Credentials.Setup(c => c.GetActivePasswordCredentialAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserCredential?)null);

        var result = await fixture.Build().Handle(
            new RequestAdminPasswordResetCommand(user.Email, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
