using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public class AcceptInvitationDirectoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string RawToken = "test-raw-token-abc123";
    private const string InvitedEmail = "invited@example.com";

    private static InvitationToken MakeInvitation() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        UserId = UserId,
        InvitedEmail = InvitedEmail,
        InvitedFullName = "Test User",
        TokenHash = InvitationTokenHasher.Hash(RawToken),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        UsedAt = null,
        RevokedAt = null,
        CompletionMethodsJson = null
    };

    private static User MakeUser() => new()
    {
        Id = UserId,
        IsActive = false
    };

    private static LoginResponseDto MakeLoginResponse() => new(
        CsrfTokenHash: "session-id",
        CsrfToken: "csrf-token",
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30)
    );

    #region Password handler

    [Fact]
    public async Task PasswordHandler_OnSuccess_CallsUpsertAsync()
    {
        // Arrange
        var invitations = new Mock<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>();
        var users = new Mock<IUserRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var legalSubmission = new Mock<ILegalAcceptanceSubmissionService>();
        var continuation = new Mock<ILoginContinuationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        var tenantContext = new Mock<ITenantContext>();
        var globalDirectory = new Mock<IGlobalEmailDirectoryRepository>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        invitations
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());

        users
            .Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser());

        passwordHasher
            .Setup(h => h.Hash(It.IsAny<string>()))
            .Returns("hashed");

        clock
            .Setup(c => c.UtcNow)
            .Returns(DateTimeOffset.UtcNow);

        legalSubmission
            .Setup(s => s.ValidateAndStageAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<LegalAcceptanceItemInput>>(),
                true,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        continuation
            .Setup(i => i.FinishAuthenticatedLoginAsync(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<LoginFinalizationMode>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(MakeLoginResponse()));

        unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        globalDirectory
            .Setup(g => g.UpsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new AcceptInvitationPasswordCommandHandler(
            invitations.Object,
            users.Object,
            passwordHasher.Object,
            legalSubmission.Object,
            continuation.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationPasswordCommand(
            RawToken: RawToken,
            Password: "NewPassword123!",
            ConfirmPassword: "NewPassword123!",
            Acceptances: [],
            IpAddress: "127.0.0.1",
            UserAgent: "test-agent");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        globalDirectory.Verify(
            g => g.UpsertAsync(InvitedEmail, TenantId, It.IsAny<CancellationToken>()),
            Times.Once,
            "UpsertAsync must be called with the invited email and tenant ID on successful acceptance");
    }

    [Fact]
    public async Task PasswordHandler_WhenInvitationNotFound_DoesNotCallUpsertAsync()
    {
        // Arrange
        var invitations = new Mock<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>();
        var users = new Mock<IUserRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var legalSubmission = new Mock<ILegalAcceptanceSubmissionService>();
        var continuation = new Mock<ILoginContinuationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        var tenantContext = new Mock<ITenantContext>();
        var globalDirectory = new Mock<IGlobalEmailDirectoryRepository>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        invitations
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);

        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var handler = new AcceptInvitationPasswordCommandHandler(
            invitations.Object,
            users.Object,
            passwordHasher.Object,
            legalSubmission.Object,
            continuation.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationPasswordCommand(
            RawToken: RawToken,
            Password: "NewPassword123!",
            ConfirmPassword: "NewPassword123!",
            Acceptances: [],
            IpAddress: null,
            UserAgent: null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();

        globalDirectory.Verify(
            g => g.UpsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "UpsertAsync must not be called when the invitation is not found");
    }

    #endregion

    #region Google handler

    [Fact]
    public async Task GoogleHandler_OnSuccess_CallsUpsertAsync()
    {
        // Arrange
        var google = new Mock<IGoogleIdTokenValidator>();
        var oauthApps = new Mock<IPlatformOAuthAppResolver>();
        var invitations = new Mock<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>();
        var policies = new Mock<ITenantAuthPolicyRepository>();
        var users = new Mock<IUserRepository>();
        var externalIdentities = new Mock<IUserExternalIdentityRepository>();
        var legalSubmission = new Mock<ILegalAcceptanceSubmissionService>();
        var continuation = new Mock<ILoginContinuationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        var tenantContext = new Mock<ITenantContext>();
        var globalDirectory = new Mock<IGlobalEmailDirectoryRepository>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        oauthApps
            .Setup(r => r.GetActiveAppForProviderAsync(
                "google",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp(
                "google",
                "test-client-id",
                "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token",
                []));

        google
            .Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload(
                Subject: "google-sub-123",
                Email: InvitedEmail,
                EmailVerified: true,
                Name: "Test User"));

        invitations
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());

        users
            .Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser());

        externalIdentities
            .Setup(e => e.GetByTenantProviderAndSubjectAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserExternalIdentity?)null);

        externalIdentities
            .Setup(e => e.AddAsync(It.IsAny<UserExternalIdentity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        legalSubmission
            .Setup(s => s.ValidateAndStageAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<LegalAcceptanceItemInput>>(),
                true,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        continuation
            .Setup(i => i.FinishAuthenticatedLoginAsync(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<LoginFinalizationMode>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(MakeLoginResponse()));

        unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        globalDirectory
            .Setup(g => g.UpsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new AcceptInvitationGoogleCommandHandler(
            google.Object,
            oauthApps.Object,
            invitations.Object,
            policies.Object,
            users.Object,
            externalIdentities.Object,
            legalSubmission.Object,
            continuation.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationGoogleCommand(
            RawToken: RawToken,
            GoogleIdToken: "google-id-token",
            Acceptances: [],
            IpAddress: "127.0.0.1",
            UserAgent: "test-agent");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        globalDirectory.Verify(
            g => g.UpsertAsync(InvitedEmail, TenantId, It.IsAny<CancellationToken>()),
            Times.Once,
            "UpsertAsync must be called with the invited email and tenant ID on successful Google acceptance");
        google.Verify(
            validator => validator.ValidateAsync(
                "google-id-token",
                "test-client-id",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GoogleHandler_WhenInvitationNotFound_DoesNotCallUpsertAsync()
    {
        // Arrange
        var google = new Mock<IGoogleIdTokenValidator>();
        var oauthApps = new Mock<IPlatformOAuthAppResolver>();
        var invitations = new Mock<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>();
        var policies = new Mock<ITenantAuthPolicyRepository>();
        var users = new Mock<IUserRepository>();
        var externalIdentities = new Mock<IUserExternalIdentityRepository>();
        var legalSubmission = new Mock<ILegalAcceptanceSubmissionService>();
        var continuation = new Mock<ILoginContinuationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        var tenantContext = new Mock<ITenantContext>();
        var globalDirectory = new Mock<IGlobalEmailDirectoryRepository>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        oauthApps
            .Setup(r => r.GetActiveAppForProviderAsync(
                "google",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp(
                "google",
                "test-client-id",
                "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token",
                []));

        google
            .Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload(
                Subject: "google-sub-123",
                Email: InvitedEmail,
                EmailVerified: true,
                Name: "Test User"));

        invitations
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);

        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var handler = new AcceptInvitationGoogleCommandHandler(
            google.Object,
            oauthApps.Object,
            invitations.Object,
            policies.Object,
            users.Object,
            externalIdentities.Object,
            legalSubmission.Object,
            continuation.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationGoogleCommand(
            RawToken: RawToken,
            GoogleIdToken: "google-id-token",
            Acceptances: [],
            IpAddress: null,
            UserAgent: null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();

        globalDirectory.Verify(
            g => g.UpsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "UpsertAsync must not be called when the invitation is not found");
    }

    [Fact]
    public async Task GoogleHandler_WhenActivePlatformOAuthAppIsMissing_FailsSafely()
    {
        var google = new Mock<IGoogleIdTokenValidator>();
        var oauthApps = new Mock<IPlatformOAuthAppResolver>();
        var tenantContext = new Mock<ITenantContext>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        oauthApps
            .Setup(r => r.GetActiveAppForProviderAsync(
                "google",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthApp?)null);

        var handler = new AcceptInvitationGoogleCommandHandler(
            google.Object,
            oauthApps.Object,
            Mock.Of<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>(),
            Mock.Of<ITenantAuthPolicyRepository>(),
            Mock.Of<IUserRepository>(),
            Mock.Of<IUserExternalIdentityRepository>(),
            Mock.Of<ILegalAcceptanceSubmissionService>(),
            Mock.Of<ILoginContinuationService>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IDateTimeProvider>(),
            tenantContext.Object,
            Mock.Of<IGlobalEmailDirectoryRepository>(),
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var result = await handler.Handle(
            new AcceptInvitationGoogleCommand(
                RawToken,
                "google-id-token",
                [],
                null,
                null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        Assert.DoesNotContain(
            "secret",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        google.Verify(
            validator => validator.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
