using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.MfaEnable;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class EnableMfaCommandHandlerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly User _user;
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserMfaRepository> _mfas = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public EnableMfaCommandHandlerTests()
    {
        _user = new User { Id = _userId, TenantId = _tenantId, Email = "owner@acme.test" };
        _currentUser.SetupGet(x => x.UserId).Returns(_userId);
        _currentUser.SetupGet(x => x.TenantId).Returns(_tenantId);
        _users.Setup(x => x.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _mfas.Setup(x => x.GetTotpAsync(_userId, true, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        _mfas.Setup(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        _encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(plain => $"encrypted:{plain}");
        _clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task NoExistingSetup_CreatesUnverifiedTotpAndReturnsSecretAndQrCodeUri()
    {
        UserMfa? added = null;
        _mfas.Setup(x => x.AddAsync(It.IsAny<UserMfa>(), It.IsAny<CancellationToken>()))
            .Callback<UserMfa, CancellationToken>((mfa, _) => added = mfa)
            .Returns(Task.CompletedTask);

        var result = await Handler().Handle(new EnableMfaCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Secret.Should().NotBeNullOrWhiteSpace();
        result.Value.QrCodeUri.Should().Contain(result.Value.Secret);
        result.Value.QrCodeUri.Should().Contain(_user.Email);

        added.Should().NotBeNull();
        added!.TenantId.Should().Be(_tenantId);
        added.MethodType.Should().Be("totp");
        added.IsVerified.Should().BeFalse();
        added.Secret.Should().Be($"encrypted:{result.Value.Secret}");
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingUnverifiedSetup_IsRemovedBeforeAddingNewOne()
    {
        var oldSetup = new UserMfa { Id = Guid.NewGuid(), UserId = _userId, MethodType = "totp", IsVerified = false };
        _mfas.Setup(x => x.GetTotpAsync(_userId, false, It.IsAny<CancellationToken>())).ReturnsAsync(oldSetup);

        var result = await Handler().Handle(new EnableMfaCommand(), default);

        result.IsSuccess.Should().BeTrue();
        _mfas.Verify(x => x.Remove(oldSetup), Times.Once);
        _mfas.Verify(x => x.AddAsync(It.IsAny<UserMfa>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifiedMfaAlreadyExists_ReturnsConflictWithoutCreatingNewSetup()
    {
        var verified = new UserMfa { Id = Guid.NewGuid(), UserId = _userId, MethodType = "totp", IsVerified = true };
        _mfas.Setup(x => x.GetTotpAsync(_userId, true, It.IsAny<CancellationToken>())).ReturnsAsync(verified);

        var result = await Handler().Handle(new EnableMfaCommand(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _mfas.Verify(x => x.AddAsync(It.IsAny<UserMfa>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private EnableMfaCommandHandler Handler() => new(
        _users.Object, _mfas.Object, _unitOfWork.Object, _currentUser.Object, _encryption.Object, _clock.Object);
}
