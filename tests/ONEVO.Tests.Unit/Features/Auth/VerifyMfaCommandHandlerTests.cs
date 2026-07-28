using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class VerifyMfaCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly User _user;
    private readonly UserMfa _mfa;
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserMfaRepository> _mfas = new();
    private readonly Mock<IMfaChallengeStore> _challenges = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ILoginContinuationService> _continuation = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();

    public VerifyMfaCommandHandlerTests()
    {
        _user = new User { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "owner@acme.test", IsActive = true };
        _mfa = new UserMfa { UserId = _user.Id, TenantId = _tenantId, Secret = "encrypted", IsVerified = true };
        _users.Setup(x => x.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _mfas.Setup(x => x.GetTotpAsync(_user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(_mfa);
        _encryption.Setup(x => x.Decrypt("encrypted")).Returns("secret");
        _tenants.Setup(x => x.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Acme",
                Slug = "acme",
                CompanySizeRange = "1-10",
                Status = TenantStatus.Active
            });
    }

    [Fact]
    public async Task ValidCode_ConsumesChallengeBeforeDelegatingToContinuation()
    {
        var state = new MfaChallengeState(
            _user.Id,
            _tenantId,
            "google_sso",
            DateTimeOffset.UtcNow.AddMinutes(5),
            0);
        _challenges.Setup(x => x.GetAsync("opaque", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _challenges.Setup(x => x.TryConsumeAsync("opaque", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _totp.Setup(x => x.Verify("secret", "123456")).Returns(true);
        _continuation
            .Setup(c => c.FinishAuthenticatedLoginAsync(_user, "google_sso", "ip", "ua", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto("csrf", "hash", null)));

        var result = await Handler(_tenantId).Handle(new VerifyMfaCommand("opaque", "123456", "ip", "ua"), default);

        result.IsSuccess.Should().BeTrue();
        _challenges.Verify(x => x.TryConsumeAsync("opaque", It.IsAny<CancellationToken>()), Times.Once);
        _continuation.Verify(
            c => c.FinishAuthenticatedLoginAsync(_user, "google_sso", "ip", "ua", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChallengeForDifferentTenant_IsRejectedWithoutUserLookup()
    {
        _challenges.Setup(x => x.GetAsync("opaque", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaChallengeState(_user.Id, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), 0));

        var result = await Handler(_tenantId).Handle(new VerifyMfaCommand("opaque", "123456", null, null), default);

        result.StatusCode.Should().Be(401);
        _users.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoVerifiedMfaRecord_ReturnsMfaNotConfigured_WithoutFallingBackToUnverifiedSetup()
    {
        var state = new MfaChallengeState(_user.Id, _tenantId, DateTimeOffset.UtcNow.AddMinutes(5), 0);
        _challenges.Setup(x => x.GetAsync("opaque", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _mfas.Setup(x => x.GetTotpAsync(_user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        var pendingSetup = new UserMfa { UserId = _user.Id, TenantId = _tenantId, Secret = "encrypted", IsVerified = false };
        _mfas.Setup(x => x.GetTotpAsync(_user.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(pendingSetup);

        var result = await Handler(_tenantId).Handle(new VerifyMfaCommand("opaque", "123456", null, null), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("MFA is not configured.");
        pendingSetup.IsVerified.Should().BeFalse();
        _mfas.Verify(x => x.GetTotpAsync(_user.Id, false, It.IsAny<CancellationToken>()), Times.Never,
            "mfa/verify must never confirm a pending setup - that is mfa/confirm-setup's job");
        _totp.Verify(x => x.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvalidCode_RegistersAttemptAndDoesNotConsumeChallenge()
    {
        var state = new MfaChallengeState(_user.Id, _tenantId, DateTimeOffset.UtcNow.AddMinutes(5), 0);
        _challenges.Setup(x => x.GetAsync("opaque", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _totp.Setup(x => x.Verify("secret", "bad")).Returns(false);

        var result = await Handler(_tenantId).Handle(new VerifyMfaCommand("opaque", "bad", null, null), default);

        result.StatusCode.Should().Be(401);
        _challenges.Verify(x => x.RegisterFailedAttemptAsync("opaque", 5, It.IsAny<CancellationToken>()), Times.Once);
        _challenges.Verify(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _continuation.Verify(
            c => c.FinishAuthenticatedLoginAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BaseHost_UsesPreTenantLookup_ThenSwitchesBeforeUserLookup()
    {
        var state = new MfaChallengeState(
            _user.Id,
            _tenantId,
            "password",
            DateTimeOffset.UtcNow.AddMinutes(5),
            0);
        _challenges
            .Setup(x => x.GetForPreTenantContinuationAsync(
                "opaque",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _challenges.Setup(x => x.TryConsumeAsync("opaque", It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _totp.Setup(x => x.Verify("secret", "123456")).Returns(true);
        _continuation
            .Setup(c => c.FinishAuthenticatedLoginAsync(
                _user,
                "password",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto("csrf", "hash", null)));

        var result = await BaseHostHandler().Handle(
            new VerifyMfaCommand("opaque", "123456", null, null),
            default);

        result.IsSuccess.Should().BeTrue();
        _challenges.Verify(
            x => x.GetForPreTenantContinuationAsync("opaque", It.IsAny<CancellationToken>()),
            Times.Once);
        _challenges.Verify(
            x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tenantSwitcher.Verify(
            x => x.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(entry => entry.TenantId == _tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private VerifyMfaCommandHandler Handler(Guid tenantId)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.IsResolved).Returns(true);
        tenant.SetupGet(x => x.ContextMode).Returns(TenantContextMode.Tenant);
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        return new VerifyMfaCommandHandler(
            _users.Object, _mfas.Object, _challenges.Object, _encryption.Object,
            _totp.Object, _continuation.Object, tenant.Object, _tenants.Object,
            _tenantSwitcher.Object);
    }

    private VerifyMfaCommandHandler BaseHostHandler()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.IsResolved).Returns(false);
        tenant.SetupGet(x => x.ContextMode).Returns(TenantContextMode.System);
        return new VerifyMfaCommandHandler(
            _users.Object, _mfas.Object, _challenges.Object, _encryption.Object,
            _totp.Object, _continuation.Object, tenant.Object, _tenants.Object,
            _tenantSwitcher.Object);
    }
}
