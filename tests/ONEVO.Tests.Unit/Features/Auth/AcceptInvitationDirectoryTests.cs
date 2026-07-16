using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
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
        var policies = new Mock<ITenantAuthPolicyRepository>();
        var users = new Mock<IUserRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var tokenIssuer = new Mock<ILoginSessionMaterialFactory>();
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

        tokenIssuer
            .Setup(i => i.PrepareAsync(
                It.IsAny<User>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
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
            policies.Object,
            users.Object,
            passwordHasher.Object,
            tokenIssuer.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationPasswordCommand(
            RawToken: RawToken,
            Password: "NewPassword123!",
            Phone: null,
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
        var policies = new Mock<ITenantAuthPolicyRepository>();
        var users = new Mock<IUserRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var tokenIssuer = new Mock<ILoginSessionMaterialFactory>();
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
            policies.Object,
            users.Object,
            passwordHasher.Object,
            tokenIssuer.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationPasswordCommand(
            RawToken: RawToken,
            Password: "NewPassword123!",
            Phone: null,
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
        var googleOptions = new Mock<IOptions<GoogleAuthOptions>>();
        var invitations = new Mock<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>();
        var policies = new Mock<ITenantAuthPolicyRepository>();
        var users = new Mock<IUserRepository>();
        var externalIdentities = new Mock<IUserExternalIdentityRepository>();
        var tokenIssuer = new Mock<ILoginSessionMaterialFactory>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        var tenantContext = new Mock<ITenantContext>();
        var globalDirectory = new Mock<IGlobalEmailDirectoryRepository>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        googleOptions
            .Setup(o => o.Value)
            .Returns(new GoogleAuthOptions
            {
                TenantInvite = new GoogleClientOptions { ClientId = "test-client-id" }
            });

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

        tokenIssuer
            .Setup(i => i.PrepareAsync(
                It.IsAny<User>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
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
            googleOptions.Object,
            invitations.Object,
            policies.Object,
            users.Object,
            externalIdentities.Object,
            tokenIssuer.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationGoogleCommand(
            RawToken: RawToken,
            GoogleIdToken: "google-id-token",
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
    }

    [Fact]
    public async Task GoogleHandler_WhenInvitationNotFound_DoesNotCallUpsertAsync()
    {
        // Arrange
        var google = new Mock<IGoogleIdTokenValidator>();
        var googleOptions = new Mock<IOptions<GoogleAuthOptions>>();
        var invitations = new Mock<ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces.IInvitationTokenRepository>();
        var policies = new Mock<ITenantAuthPolicyRepository>();
        var users = new Mock<IUserRepository>();
        var externalIdentities = new Mock<IUserExternalIdentityRepository>();
        var tokenIssuer = new Mock<ILoginSessionMaterialFactory>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        var tenantContext = new Mock<ITenantContext>();
        var globalDirectory = new Mock<IGlobalEmailDirectoryRepository>();

        tenantContext.Setup(t => t.IsResolved).Returns(true);
        tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        googleOptions
            .Setup(o => o.Value)
            .Returns(new GoogleAuthOptions
            {
                TenantInvite = new GoogleClientOptions { ClientId = "test-client-id" }
            });

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
            googleOptions.Object,
            invitations.Object,
            policies.Object,
            users.Object,
            externalIdentities.Object,
            tokenIssuer.Object,
            unitOfWork.Object,
            clock.Object,
            tenantContext.Object,
            globalDirectory.Object,
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPositionRepository>());

        var command = new AcceptInvitationGoogleCommand(
            RawToken: RawToken,
            GoogleIdToken: "google-id-token",
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
}
