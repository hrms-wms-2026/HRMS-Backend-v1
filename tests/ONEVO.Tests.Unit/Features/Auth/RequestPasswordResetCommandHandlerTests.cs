using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class RequestPasswordResetCommandHandlerTests
{
    private sealed class HandlerFixture
    {
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IPasswordResetTokenRepository> PasswordResetTokens { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ISecureTokenGenerator> TokenService { get; } = new();
        public Mock<IOutboxWriter> Outbox { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<ITenantContext> TenantContext { get; } = new();

        public HandlerFixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-27T12:00:00Z"));
            PasswordResetTokens
                .Setup(r => r.ListValidByUserIdAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PasswordResetToken>());
            TokenService.Setup(t => t.GenerateOpaqueToken()).Returns("raw-token-1");
            TokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns((string raw) => $"hash-of-{raw}");
            Outbox
                .Setup(o => o.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<PasswordResetEmailPayload>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            TenantContext.Setup(t => t.IsResolved).Returns(true);
            TenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
            TenantContext.Setup(t => t.Slug).Returns("acme");
        }

        public RequestPasswordResetCommandHandler Build() => new(
            Users.Object,
            PasswordResetTokens.Object,
            UnitOfWork.Object,
            TokenService.Object,
            Outbox.Object,
            Clock.Object,
            TenantContext.Object);

        public void VerifyNeverEnqueued() =>
            Outbox.Verify(
                o => o.EnqueueAsync(
                    It.IsAny<string>(), It.IsAny<PasswordResetEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
    }

    private static User BuildActiveUser(Guid tenantId, string email) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email,
        FirstName = "Test",
        LastName = "User",
        PasswordHash = "irrelevant-hash",
        IsActive = true
    };

    [Fact]
    public async Task Handle_WithExistingUser_CreatesTokenAndEnqueuesEmail()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var user = BuildActiveUser(tenantId, "owner@acme.test");
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.Users
            .Setup(u => u.GetByTenantAndEmailAsync(tenantId, "owner@acme.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        PasswordResetToken? added = null;
        fixture.PasswordResetTokens
            .Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
            .Callback<PasswordResetToken, CancellationToken>((t, _) => added = t)
            .Returns(Task.CompletedTask);

        var result = await fixture.Build().Handle(
            new RequestPasswordResetCommand("owner@acme.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.UserId.Should().Be(user.Id);
        added.TenantId.Should().Be(tenantId);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Outbox.Verify(
            o => o.EnqueueAsync(
                OutboxMessageTypes.PasswordResetEmail,
                It.Is<PasswordResetEmailPayload>(p =>
                    p.TenantId == tenantId
                    && p.UserId == user.Id
                    && p.Email == "owner@acme.test"
                    && p.RawToken == "raw-token-1"
                    && p.TenantSlug == "acme"),
                tenantId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsSuccessAndCreatesNoTokenOrEmail()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.Users
            .Setup(u => u.GetByTenantAndEmailAsync(tenantId, "unknown@acme.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await fixture.Build().Handle(
            new RequestPasswordResetCommand("unknown@acme.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.PasswordResetTokens.Verify(
            r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNeverEnqueued();
    }

    [Fact]
    public async Task Handle_WithInactiveUser_ReturnsSuccessAndCreatesNoTokenOrEmail()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var inactiveUser = BuildActiveUser(tenantId, "inactive@acme.test");
        inactiveUser.IsActive = false;
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.Users
            .Setup(u => u.GetByTenantAndEmailAsync(tenantId, "inactive@acme.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveUser);

        var result = await fixture.Build().Handle(
            new RequestPasswordResetCommand("inactive@acme.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.PasswordResetTokens.Verify(
            r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNeverEnqueued();
    }

    [Fact]
    public async Task Handle_WhenTenantContextNotResolved_ReturnsSuccessNoOp()
    {
        var fixture = new HandlerFixture();
        fixture.TenantContext.Setup(t => t.IsResolved).Returns(false);

        var result = await fixture.Build().Handle(
            new RequestPasswordResetCommand("anyone@acme.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Users.Verify(
            u => u.GetByTenantAndEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.VerifyNeverEnqueued();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WhenTenantResolvedButSlugMissing_ReturnsSuccessAndFailsClosed(string? slug)
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var user = BuildActiveUser(tenantId, "owner@acme.test");
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.TenantContext.Setup(t => t.Slug).Returns(slug);
        fixture.Users
            .Setup(u => u.GetByTenantAndEmailAsync(tenantId, "owner@acme.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await fixture.Build().Handle(
            new RequestPasswordResetCommand("owner@acme.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.PasswordResetTokens.Verify(
            r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNeverEnqueued();
    }
}
