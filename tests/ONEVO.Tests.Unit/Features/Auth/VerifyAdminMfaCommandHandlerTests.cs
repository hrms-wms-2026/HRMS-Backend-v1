using FluentAssertions;
using Moq;

using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.AdminMfaVerify;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class VerifyAdminMfaCommandHandlerTests
{
    private readonly Mock<IPlatformMfaChallengeStore> _mfaChallenges = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IPlatformPermissionResolver> _resolver = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    public VerifyAdminMfaCommandHandlerTests()
    {
        _clock.Setup(value => value.UtcNow).Returns(_now);
        _tokens.Setup(value => value.GenerateCsrfToken()).Returns("raw-csrf");
        _tokens.Setup(value => value.HashToken(It.IsAny<string>())).Returns("hashed-csrf");
    }

    [Fact]
    public async Task Handle_ValidCode_ReturnsResolvedPermissions()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@onevo.io",
            FullName = "Platform Admin",
            Status = PlatformUser.StatusActive,
            MfaSecret = "encrypted-secret",
        };

        _mfaChallenges.Setup(value => value.GetAsync("challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformMfaChallengeState(user.Id, _now.AddMinutes(5), 0));
        _users.Setup(value => value.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _encryption.Setup(value => value.Decrypt("encrypted-secret")).Returns("decrypted-secret");
        _totpService.Setup(value => value.Verify("decrypted-secret", "123456")).Returns(true);
        _mfaChallenges.Setup(value => value.TryConsumeAsync("challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformMfaChallengeState(user.Id, _now.AddMinutes(5), 0));
        _resolver.Setup(value => value.ResolveActiveUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformAccessProfile
            {
                UserId = user.Id,
                Email = user.Email,
                Status = user.Status,
                RoleNames = { "Platform Super Admin" },
                PermissionCodes = { "platform.accounts.read", "platform.accounts.manage" },
            });

        var handler = new VerifyAdminMfaCommandHandler(
            _mfaChallenges.Object,
            _users.Object,
            _encryption.Object,
            _totpService.Object,
            _tokens.Object,
            _resolver.Object,
            _clock.Object,
            _uow.Object);

        var result = await handler.Handle(
            new VerifyAdminMfaCommand("challenge", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Permissions.Should().BeEquivalentTo(
            new[] { "platform.accounts.read", "platform.accounts.manage" });
    }

    [Fact]
    public async Task Handle_InvalidCode_ReturnsUnauthorized()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@onevo.io",
            FullName = "Platform Admin",
            Status = PlatformUser.StatusActive,
            MfaSecret = "encrypted-secret",
        };

        _mfaChallenges.Setup(value => value.GetAsync("challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformMfaChallengeState(user.Id, _now.AddMinutes(5), 0));
        _users.Setup(value => value.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _encryption.Setup(value => value.Decrypt("encrypted-secret")).Returns("decrypted-secret");
        _totpService.Setup(value => value.Verify("decrypted-secret", "000000")).Returns(false);
        _mfaChallenges.Setup(value => value.RegisterFailedAttemptAsync(
                "challenge", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new VerifyAdminMfaCommandHandler(
            _mfaChallenges.Object,
            _users.Object,
            _encryption.Object,
            _totpService.Object,
            _tokens.Object,
            _resolver.Object,
            _clock.Object,
            _uow.Object);

        var result = await handler.Handle(
            new VerifyAdminMfaCommand("challenge", "000000"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
