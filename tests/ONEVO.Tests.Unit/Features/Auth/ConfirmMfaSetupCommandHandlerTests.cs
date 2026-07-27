using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.MfaConfirmSetup;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ConfirmMfaSetupCommandHandlerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IUserMfaRepository> _mfas = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<ITotpService> _totp = new();

    public ConfirmMfaSetupCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.UserId).Returns(_userId);
        _currentUser.SetupGet(x => x.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task ValidCode_MarksPendingRecordVerifiedAndSaves()
    {
        var pending = new UserMfa
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            TenantId = _tenantId,
            MethodType = "totp",
            Secret = "encrypted-secret",
            IsVerified = false
        };
        _mfas.Setup(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>())).ReturnsAsync(pending);
        _encryption.Setup(x => x.Decrypt("encrypted-secret")).Returns("plain-secret");
        _totp.Setup(x => x.Verify("plain-secret", "123456")).Returns(true);

        var result = await Handler().Handle(new ConfirmMfaSetupCommand("123456"), default);

        result.IsSuccess.Should().BeTrue();
        pending.IsVerified.Should().BeTrue();
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidCode_ReturnsFailureAndLeavesRecordUnverified()
    {
        var pending = new UserMfa
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            TenantId = _tenantId,
            MethodType = "totp",
            Secret = "encrypted-secret",
            IsVerified = false
        };
        _mfas.Setup(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>())).ReturnsAsync(pending);
        _encryption.Setup(x => x.Decrypt("encrypted-secret")).Returns("plain-secret");
        _totp.Setup(x => x.Verify("plain-secret", "000000")).Returns(false);

        var result = await Handler().Handle(new ConfirmMfaSetupCommand("000000"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid MFA code.");
        pending.IsVerified.Should().BeFalse();
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoPendingSetup_ReturnsSafeFailureWithoutDecryptingOrVerifying()
    {
        _mfas.Setup(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);

        var result = await Handler().Handle(new ConfirmMfaSetupCommand("123456"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("No pending MFA setup exists.");
        _encryption.Verify(x => x.Decrypt(It.IsAny<string>()), Times.Never);
        _totp.Verify(x => x.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AlwaysLooksUpPendingSetupForCurrentUserOnly()
    {
        _mfas.Setup(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);

        await Handler().Handle(new ConfirmMfaSetupCommand("123456"), default);

        _mfas.Verify(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>()), Times.Once);
        _mfas.Verify(x => x.GetTotpAsync(It.Is<Guid>(id => id != _userId), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ConfirmMfaSetupCommandHandler Handler() => new(
        _mfas.Object, _unitOfWork.Object, _currentUser.Object, _encryption.Object, _totp.Object);
}
