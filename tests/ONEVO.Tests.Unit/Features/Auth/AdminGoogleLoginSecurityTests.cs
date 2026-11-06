using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Security audit 2026-11-05, finding F-01: Google admin sign-in must apply the same MFA gate as
/// password sign-in, must reject Google identities whose email Google has not verified, and must
/// never bind a Google account to a platform user by email address alone.
/// </summary>
public sealed class AdminGoogleLoginSecurityTests
{
    private const string IdToken = "google-id-token";
    private const string ClientId = "google-client-id";
    private const string GoogleSub = "google-sub-123";
    private const string Email = "admin@onexso.test";

    private readonly Mock<IGoogleIdTokenValidator> _google = new();
    private readonly Mock<IPlatformOAuthAppResolver> _oauthApps = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformPermissionResolver> _resolver = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<IPlatformMfaChallengeStore> _mfaChallenges = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly DateTimeOffset _now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    public AdminGoogleLoginSecurityTests()
    {
        _clock.Setup(value => value.UtcNow).Returns(_now);
        _tokens.Setup(value => value.GenerateCsrfToken()).Returns("raw-csrf");
        _tokens.Setup(value => value.HashToken(It.IsAny<string>())).Returns("hashed-csrf");
        _oauthApps
            .Setup(value => value.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp(
                "google",
                ClientId,
                "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token",
                []));
        _uow.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
    }

    [Fact]
    public async Task MfaEnrolledAdmin_BoundGoogleAccount_GetsMfaChallengeInsteadOfSession()
    {
        var user = ActiveUser(googleSub: GoogleSub, mfaStatus: PlatformUser.MfaEnrolled);
        GoogleReturns(emailVerified: true);
        _users.Setup(value => value.GetByGoogleSubAsync(GoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        ProfileIsActive(user);
        _mfaChallenges
            .Setup(value => value.CreateAsync(user.Id, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("mfa-challenge");

        var result = await Build().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresMfa.Should().BeTrue();
        result.Value.MfaSessionToken.Should().Be("mfa-challenge");
        result.Value.CsrfToken.Should().BeEmpty();
        result.Value.CsrfTokenHash.Should().BeEmpty();
        result.Value.PlatformUserId.Should().Be(Guid.Empty);
        result.Value.ExpiresAt.Should().BeNull();
        _tokens.Verify(value => value.GenerateCsrfToken(), Times.Never);
        user.LastLoginAt.Should().BeNull();
    }

    [Fact]
    public async Task NotEnrolledAdmin_BoundGoogleAccount_GetsSession()
    {
        // Parity with password login while MFA stays optional (F-02 tracks making it mandatory).
        var user = ActiveUser(googleSub: GoogleSub, mfaStatus: PlatformUser.MfaNotEnrolled);
        GoogleReturns(emailVerified: true);
        _users.Setup(value => value.GetByGoogleSubAsync(GoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        ProfileIsActive(user);

        var result = await Build().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresMfa.Should().BeFalse();
        result.Value.PlatformUserId.Should().Be(user.Id);
        result.Value.CsrfToken.Should().Be("raw-csrf");
    }

    [Fact]
    public async Task UnverifiedGoogleEmail_IsRejectedBeforeAnyAccountLookup()
    {
        GoogleReturns(emailVerified: false);

        var result = await Build().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        _users.Verify(value => value.GetByGoogleSubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(value => value.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmailMatch_AccountAlreadyBoundToAnotherGoogleSub_IsForbidden()
    {
        var user = ActiveUser(googleSub: "a-different-google-sub", mfaStatus: PlatformUser.MfaNotEnrolled);
        GoogleReturns(emailVerified: true);
        _users.Setup(value => value.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        ProfileIsActive(user);

        var result = await Build().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        user.GoogleSub.Should().Be("a-different-google-sub");
        _tokens.Verify(value => value.GenerateCsrfToken(), Times.Never);
    }

    [Fact]
    public async Task EmailMatch_UnboundAccountWithoutMfa_IsForbiddenAndNotLinked()
    {
        var user = ActiveUser(googleSub: null, mfaStatus: PlatformUser.MfaNotEnrolled);
        GoogleReturns(emailVerified: true);
        _users.Setup(value => value.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        ProfileIsActive(user);

        var result = await Build().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        user.GoogleSub.Should().BeNull();
        _tokens.Verify(value => value.GenerateCsrfToken(), Times.Never);
    }

    [Fact]
    public async Task EmailMatch_UnboundAccountWithMfa_GetsLinkChallengeAndIsNotLinkedYet()
    {
        var user = ActiveUser(googleSub: null, mfaStatus: PlatformUser.MfaEnrolled);
        GoogleReturns(emailVerified: true);
        _users.Setup(value => value.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        ProfileIsActive(user);
        _mfaChallenges
            .Setup(value => value.CreateForGoogleLinkAsync(
                user.Id, GoogleSub, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("link-challenge");

        var result = await Build().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresMfa.Should().BeTrue();
        result.Value.MfaSessionToken.Should().Be("link-challenge");
        result.Value.CsrfToken.Should().BeEmpty();
        user.GoogleSub.Should().BeNull();
        _tokens.Verify(value => value.GenerateCsrfToken(), Times.Never);
    }

    private AdminGoogleLoginCommandHandler Build() => new(
        _google.Object,
        _oauthApps.Object,
        _tokens.Object,
        _clock.Object,
        _users.Object,
        _resolver.Object,
        _authEvents.Object,
        _mfaChallenges.Object,
        _uow.Object);

    private static AdminGoogleLoginCommand Command() => new(IdToken, "127.0.0.1", "test-agent");

    private void GoogleReturns(bool emailVerified) =>
        _google.Setup(value => value.ValidateAsync(IdToken, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload(GoogleSub, Email, emailVerified, "Platform Admin"));

    private static PlatformUser ActiveUser(string? googleSub, string mfaStatus) => new()
    {
        Id = Guid.NewGuid(),
        Email = Email,
        FullName = "Platform Admin",
        Status = PlatformUser.StatusActive,
        GoogleSub = googleSub,
        MfaStatus = mfaStatus,
        MfaSecret = mfaStatus == PlatformUser.MfaEnrolled ? "encrypted-secret" : null,
    };

    private void ProfileIsActive(PlatformUser user) =>
        _resolver.Setup(value => value.ResolveActiveUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformAccessProfile
            {
                UserId = user.Id,
                Email = user.Email,
                Status = user.Status,
                RoleNames = { "Platform Super Admin" },
                PermissionCodes = { "platform.accounts.read" },
            });
}
