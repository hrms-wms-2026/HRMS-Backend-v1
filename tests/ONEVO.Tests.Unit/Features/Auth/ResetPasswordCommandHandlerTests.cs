using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ResetPasswordCommandHandlerTests
{
    private const string GenericTokenError = "Invalid or expired reset token.";

    private sealed class HandlerFixture
    {
        public Mock<IPasswordResetTokenRepository> PasswordResetTokens { get; } = new();
        public Mock<IRefreshTokenRepository> RefreshTokens { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ISecureTokenGenerator> TokenService { get; } = new();
        public Mock<IPasswordHasher> PasswordHasher { get; } = new();
        public Mock<IPermissionVersionService> PermVersionService { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<ITenantContext> TenantContext { get; } = new();

        public HandlerFixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-27T12:00:00Z"));
            TokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns((string raw) => $"hash-of-{raw}");
            PasswordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns((string pw) => $"hashed-{pw}");
            TenantContext.Setup(t => t.IsResolved).Returns(true);
            TenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
            RefreshTokens
                .Setup(r => r.ListActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<RefreshToken>());

            // Mirrors the real UnitOfWork's commit-on-return semantics without a real database:
            // just run the operation inline.
            UnitOfWork
                .Setup(u => u.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((operation, ct) => operation(ct));
        }

        public ResetPasswordCommandHandler Build() => new(
            PasswordResetTokens.Object,
            RefreshTokens.Object,
            Users.Object,
            UnitOfWork.Object,
            TokenService.Object,
            PasswordHasher.Object,
            PermVersionService.Object,
            Clock.Object,
            TenantContext.Object);

        public void SetupConsumedToken(Guid tenantId, Guid? userId) =>
            PasswordResetTokens
                .Setup(r => r.TryConsumeResetTokenAsync(
                    It.IsAny<string>(), tenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);
    }

    private static User BuildActiveUser(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = "user@acme.test",
        FirstName = "Test",
        LastName = "User",
        PasswordHash = "old-hash",
        IsActive = true
    };

    [Fact]
    public async Task Handle_ValidToken_ConsumesAtomicallyAndUpdatesPassword()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var user = BuildActiveUser(tenantId);
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, user.Id);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("hashed-NewPassword1");
        user.MustChangePassword.Should().BeFalse();
        user.PasswordSetByAdmin.Should().BeFalse();
        user.TemporaryPasswordExpiresAt.Should().BeNull();
        fixture.PasswordResetTokens.Verify(
            r => r.TryConsumeResetTokenAsync(
                "hash-of-raw-token", tenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UsedToken_ReturnsGenericInvalidTokenError()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, null);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        fixture.Users.Verify(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsGenericInvalidTokenError()
    {
        // The repository's atomic UPDATE ... WHERE expires_at > now guard already excludes expired
        // tokens, so from the handler's perspective this looks identical to any other non-consumed
        // token: TryConsumeResetTokenAsync returns null.
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, null);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
    }

    [Fact]
    public async Task Handle_WrongTenantToken_ReturnsGenericInvalidTokenError()
    {
        // The repository call is scoped to the resolved tenant id; a token belonging to a different
        // tenant never matches the atomic UPDATE's WHERE tenant_id = @tenantId guard.
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, null);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        fixture.PasswordResetTokens.Verify(
            r => r.TryConsumeResetTokenAsync(
                It.IsAny<string>(), tenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserMissingAfterConsumption_BurnsTokenAndReturnsGenericError()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var missingUserId = Guid.NewGuid();
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, missingUserId);
        fixture.Users.Setup(u => u.GetByIdAsync(missingUserId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        // Documented fail-safe tradeoff: TryConsumeResetTokenAsync already ran (and the surrounding
        // transaction still commits), so the token is burned even though no password mutation
        // happens here.
        fixture.PasswordResetTokens.Verify(
            r => r.TryConsumeResetTokenAsync(
                It.IsAny<string>(), tenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveUserAfterConsumption_BurnsTokenAndReturnsGenericError()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var inactiveUser = BuildActiveUser(tenantId);
        inactiveUser.IsActive = false;
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, inactiveUser.Id);
        fixture.Users.Setup(u => u.GetByIdAsync(inactiveUser.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inactiveUser);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidToken_RevokesActiveRefreshTokens()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var user = BuildActiveUser(tenantId);
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, user.Id);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var activeToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            TokenHash = "rt-hash",
            RevokedAt = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        fixture.RefreshTokens
            .Setup(r => r.ListActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { activeToken });

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        activeToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidToken_IncrementsPermissionVersion()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var user = BuildActiveUser(tenantId);
        fixture.TenantContext.Setup(t => t.TenantId).Returns(tenantId);
        fixture.SetupConsumedToken(tenantId, user.Id);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.PermVersionService.Verify(
            p => p.IncrementVersionAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TenantContextNotResolved_ReturnsGenericErrorWithoutTouchingRepository()
    {
        var fixture = new HandlerFixture();
        fixture.TenantContext.Setup(t => t.IsResolved).Returns(false);

        var result = await fixture.Build().Handle(
            new ResetPasswordCommand("raw-token", "NewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GenericTokenError);
        fixture.PasswordResetTokens.Verify(
            r => r.TryConsumeResetTokenAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
