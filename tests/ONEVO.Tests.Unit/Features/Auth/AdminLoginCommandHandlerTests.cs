using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class AdminLoginCommandHandlerTests
{
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformUserCredentialRepository> _credentials = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IPlatformPermissionResolver> _resolver = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<IPlatformMfaChallengeStore> _mfaChallenges = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<PlatformAuthEvent> _writtenEvents = [];
    private readonly DateTimeOffset _now = new(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);

    public AdminLoginCommandHandlerTests()
    {
        _clock.Setup(value => value.UtcNow).Returns(_now);
        _tokens.Setup(value => value.GenerateCsrfToken()).Returns("raw-csrf");
        _tokens.Setup(value => value.HashToken("raw-csrf")).Returns("hashed-csrf");
        _authEvents
            .Setup(value => value.AddAsync(It.IsAny<PlatformAuthEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformAuthEvent, CancellationToken>((value, _) => _writtenEvents.Add(value))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidDatabaseCredential_SucceedsAndResetsCredentialState()
    {
        var setup = SetupActiveUserAndCredential();
        setup.Credential.FailedLoginCount = 3;
        setup.Credential.LockedUntil = _now.AddMinutes(-1);
        _passwordHasher.Setup(value => value.Verify("correct", "stored-hash")).Returns(true);

        var result = await Handler().Handle(
            new AdminLoginCommand(setup.User.Email, "correct"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PlatformUserId.Should().Be(setup.User.Id);
        setup.Credential.FailedLoginCount.Should().Be(0);
        setup.Credential.LockedUntil.Should().BeNull();
        setup.Credential.LastUsedAt.Should().Be(_now);
        setup.User.LastLoginAt.Should().Be(_now);
        _writtenEvents.Should().ContainSingle(value => value.EventType == PlatformAuthEvent.LoginSucceeded);
    }

    [Fact]
    public async Task Handle_WrongPassword_IncrementsFailureWithoutExposingPassword()
    {
        var setup = SetupActiveUserAndCredential();
        _passwordHasher.Setup(value => value.Verify("wrong-password", "stored-hash")).Returns(false);

        var result = await Handler().Handle(
            new AdminLoginCommand(setup.User.Email, "wrong-password"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        setup.Credential.FailedLoginCount.Should().Be(1);
        var authEvent = _writtenEvents.Should().ContainSingle().Which;
        authEvent.MetadataJson.Should().NotContain("wrong-password");
        authEvent.MetadataJson.Should().NotContain("stored-hash");
    }

    [Fact]
    public async Task Handle_FifthWrongPassword_AppliesLockout()
    {
        var setup = SetupActiveUserAndCredential();
        setup.Credential.FailedLoginCount = 4;

        await Handler().Handle(
            new AdminLoginCommand(setup.User.Email, "wrong"),
            CancellationToken.None);

        setup.Credential.FailedLoginCount.Should().Be(5);
        setup.Credential.LockedUntil.Should().Be(_now.AddMinutes(15));
    }

    [Fact]
    public async Task Handle_ActiveLockout_DoesNotVerifyPassword()
    {
        var setup = SetupActiveUserAndCredential();
        setup.Credential.LockedUntil = _now.AddMinutes(1);

        var result = await Handler().Handle(
            new AdminLoginCommand(setup.User.Email, "correct"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _passwordHasher.Verify(value => value.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingDatabaseCredential_ReturnsGenericUnauthorized()
    {
        var user = SetupActiveUser();
        _credentials.Setup(value => value.GetActivePasswordCredentialAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserCredential?)null);

        var result = await Handler().Handle(
            new AdminLoginCommand(user.Email, "password"),
            CancellationToken.None);

        result.StatusCode.Should().Be(401);
        result.Error.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Handle_ResponseNeverContainsCredentialOrBearerMaterial()
    {
        var setup = SetupActiveUserAndCredential();
        _passwordHasher.Setup(value => value.Verify("correct", "stored-hash")).Returns(true);

        var result = await Handler().Handle(
            new AdminLoginCommand(setup.User.Email, "correct"),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value!.ToSessionResponse());

        json.Should().NotContainEquivalentOf("password");
        json.Should().NotContainEquivalentOf("access_token");
        json.Should().NotContainEquivalentOf("refresh_token");
        json.Should().NotContainEquivalentOf("jwt");
        json.Should().NotContain("stored-hash");
        json.Should().NotContain("hashed-csrf");
    }

    [Fact]
    public async Task Handle_MfaEnrolledUser_ReturnsChallengeInsteadOfSession()
    {
        var setup = SetupActiveUserAndCredential();
        setup.User.MfaStatus = PlatformUser.MfaEnrolled;
        _passwordHasher.Setup(value => value.Verify("correct", "stored-hash")).Returns(true);
        _mfaChallenges
            .Setup(value => value.CreateAsync(setup.User.Id, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-mfa-challenge");

        var result = await Handler().Handle(
            new AdminLoginCommand(setup.User.Email, "correct"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresMfa.Should().BeTrue();
        result.Value!.MfaSessionToken.Should().Be("raw-mfa-challenge");
        result.Value!.CsrfToken.Should().BeEmpty();
        result.Value!.PlatformUserId.Should().Be(Guid.Empty);
        var sessionResponse = result.Value!.ToSessionResponse();
        sessionResponse.MfaRequired.Should().BeTrue();
        sessionResponse.Email.Should().BeEmpty();
    }

    private AdminLoginCommandHandler Handler()
    {
        return new AdminLoginCommandHandler(
            _tokens.Object,
            _clock.Object,
            _users.Object,
            _credentials.Object,
            _passwordHasher.Object,
            _resolver.Object,
            _authEvents.Object,
            _mfaChallenges.Object,
            _uow.Object);
    }

    private PlatformUser SetupActiveUser()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@onevo.io",
            FullName = "Platform Admin",
            Status = PlatformUser.StatusActive
        };
        _users.Setup(value => value.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _resolver.Setup(value => value.ResolveActiveUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformAccessProfile
            {
                UserId = user.Id,
                Email = user.Email,
                Status = user.Status,
                RoleNames = { "Platform Super Admin" }
            });
        return user;
    }

    private (PlatformUser User, PlatformUserCredential Credential) SetupActiveUserAndCredential()
    {
        var user = SetupActiveUser();
        var credential = new PlatformUserCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            PasswordHash = "stored-hash",
            PasswordAlgorithm = PlatformUserCredential.BCryptAlgorithm
        };
        _credentials.Setup(value => value.GetActivePasswordCredentialAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        return (user, credential);
    }
}
