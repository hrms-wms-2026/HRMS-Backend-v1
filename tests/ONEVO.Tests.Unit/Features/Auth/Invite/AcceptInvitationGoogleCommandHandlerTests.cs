using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Invite;

/// <summary>
/// Covers the invitation-usability, tenant-scoping, Google email-domain-mismatch policy,
/// external-identity-conflict, and position-role branches of AcceptInvitationGoogleCommandHandler
/// that AcceptInvitationDirectoryTests (directory-upsert focused) does not exercise.
/// </summary>
public class AcceptInvitationGoogleCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
    private const string RawToken = "raw-token-google-flow";
    private const string InvitedEmail = "invited@example.com";
    private const string ClientId = "test-client-id";

    private readonly Mock<IGoogleIdTokenValidator> _google = new();
    private readonly Mock<IPlatformOAuthAppResolver> _oauthApps = new();
    private readonly Mock<IInvitationTokenRepository> _invitations = new();
    private readonly Mock<ITenantAuthPolicyRepository> _policies = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserExternalIdentityRepository> _externalIdentities = new();
    private readonly Mock<ILegalAcceptanceSubmissionService> _legalSubmission = new();
    private readonly Mock<ILoginContinuationService> _continuation = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IGlobalEmailDirectoryRepository> _globalDirectory = new();
    private readonly Mock<IUserRoleRepository> _userRoles = new();
    private readonly Mock<IPositionRepository> _positions = new();

    public AcceptInvitationGoogleCommandHandlerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _tenantContext.Setup(t => t.IsResolved).Returns(true);
        _tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        _tenantContext.Setup(t => t.TenantId).Returns(TenantId);
        _oauthApps
            .Setup(r => r.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp(
                "google", ClientId, "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token", []));
        _externalIdentities
            .Setup(e => e.GetByTenantProviderAndSubjectAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserExternalIdentity?)null);
        _externalIdentities
            .Setup(e => e.AddAsync(It.IsAny<UserExternalIdentity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _globalDirectory
            .Setup(g => g.UpsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _legalSubmission
            .Setup(s => s.ValidateAndStageAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<LegalAcceptanceItemInput>>(),
                true, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _continuation
            .Setup(c => c.FinishAuthenticatedLoginAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<LoginFinalizationMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(
                new LoginResponseDto("csrf-raw", "csrf-hash", Now.AddMinutes(30))));
    }

    private static InvitationToken MakeInvitation(
        Guid? tenantId = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? usedAt = null,
        DateTimeOffset? revokedAt = null,
        Guid? positionId = null,
        bool? allowGoogleEmailMismatch = null,
        string? allowedEmailDomainsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId ?? TenantId,
        UserId = UserId,
        PositionId = positionId,
        InvitedEmail = InvitedEmail,
        InvitedFullName = "Test User",
        TokenHash = InvitationTokenHasher.Hash(RawToken),
        ExpiresAt = expiresAt ?? Now.AddDays(1),
        UsedAt = usedAt,
        RevokedAt = revokedAt,
        AllowGoogleEmailMismatch = allowGoogleEmailMismatch,
        AllowedEmailDomainsJson = allowedEmailDomainsJson
    };

    private void SetupGoogleValidation(string email, bool emailVerified = true, string subject = "google-sub-1") =>
        _google
            .Setup(g => g.ValidateAsync(It.IsAny<string>(), ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload(subject, email, emailVerified, "Test User"));

    private AcceptInvitationGoogleCommandHandler BuildSut() => new(
        _google.Object, _oauthApps.Object, _invitations.Object, _policies.Object, _users.Object,
        _externalIdentities.Object, _legalSubmission.Object, _continuation.Object, _unitOfWork.Object,
        _clock.Object, _tenantContext.Object, _globalDirectory.Object, _userRoles.Object, _positions.Object);

    private static AcceptInvitationGoogleCommand MakeCommand() => new(
        RawToken: RawToken,
        GoogleIdToken: "google-id-token",
        Acceptances: [],
        IpAddress: "127.0.0.1",
        UserAgent: "test-agent");

    [Fact]
    public async Task Handle_InvalidGoogleToken_ReturnsFailure401()
    {
        _google
            .Setup(g => g.ValidateAsync(It.IsAny<string>(), ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleIdTokenPayload?)null);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        _invitations.Verify(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnverifiedGoogleEmail_ReturnsForbidden403()
    {
        SetupGoogleValidation(InvitedEmail, emailVerified: false);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ExpiredInvitation_ReturnsFailure400()
    {
        SetupGoogleValidation(InvitedEmail);
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(expiresAt: Now.AddDays(-1)));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task Handle_RevokedInvitation_ReturnsFailure400()
    {
        SetupGoogleValidation(InvitedEmail);
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(revokedAt: Now.AddHours(-1)));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("revoked");
    }

    [Fact]
    public async Task Handle_AlreadyUsedInvitation_ReturnsFailure400()
    {
        SetupGoogleValidation(InvitedEmail);
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(usedAt: Now.AddHours(-1)));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("already been accepted");
    }

    [Fact]
    public async Task Handle_InvitationBelongsToDifferentTenant_ReturnsNotFound()
    {
        SetupGoogleValidation(InvitedEmail);
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(tenantId: Guid.NewGuid()));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_EmailMismatch_NotAllowed_ReturnsForbidden()
    {
        SetupGoogleValidation("someone-else@example.com");
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(allowGoogleEmailMismatch: false));
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantAuthPolicy?)null);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("must match the invited email");
    }

    [Fact]
    public async Task Handle_EmailMismatch_AllowedButNoDomainsConfigured_ReturnsFailure403()
    {
        SetupGoogleValidation("someone-else@example.com");
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(allowGoogleEmailMismatch: true, allowedEmailDomainsJson: null));
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantAuthPolicy?)null);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("no permitted domains");
    }

    [Fact]
    public async Task Handle_EmailMismatch_DomainNotInAllowedList_ReturnsForbidden()
    {
        SetupGoogleValidation("someone-else@other-domain.com");
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(
                allowGoogleEmailMismatch: true,
                allowedEmailDomainsJson: JsonSerializer.Serialize(new[] { "acme.com" })));
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantAuthPolicy?)null);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("domain is not allowed");
    }

    [Fact]
    public async Task Handle_EmailMismatch_DomainInAllowedList_Succeeds()
    {
        SetupGoogleValidation("someone-else@acme.com");
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(
                allowGoogleEmailMismatch: true,
                allowedEmailDomainsJson: JsonSerializer.Serialize(new[] { "acme.com" })));
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantAuthPolicy?)null);
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExternalIdentityAlreadyLinkedToDifferentUser_ReturnsConflict()
    {
        SetupGoogleValidation(InvitedEmail, subject: "google-sub-conflict");
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });
        _externalIdentities
            .Setup(e => e.GetByTenantProviderAndSubjectAsync(TenantId, "google", "google-sub-conflict", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserExternalIdentity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                UserId = Guid.NewGuid(),
                Provider = "google",
                ProviderSubject = "google-sub-conflict"
            });

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExternalIdentityAlreadyLinkedToSameUser_UpdatesInPlace_DoesNotAddNew()
    {
        SetupGoogleValidation(InvitedEmail, subject: "google-sub-same");
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });
        _externalIdentities
            .Setup(e => e.GetByTenantProviderAndSubjectAsync(TenantId, "google", "google-sub-same", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserExternalIdentity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                UserId = UserId,
                Provider = "google",
                ProviderSubject = "google-sub-same"
            });

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _externalIdentities.Verify(
            e => e.AddAsync(It.IsAny<UserExternalIdentity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PositionWithDefaultRole_AssignsUserRole()
    {
        var positionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        SetupGoogleValidation(InvitedEmail);
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(positionId: positionId));
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });
        _positions.Setup(p => p.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position
            {
                Id = positionId,
                DefaultRoleId = roleId
            });

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userRoles.Verify(
            r => r.AddAsync(
                It.Is<UserRole>(ur => ur.UserId == UserId && ur.RoleId == roleId && ur.TenantId == TenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LegalAcceptanceValidationFails_PropagatesFailure_AndDoesNotPersist()
    {
        SetupGoogleValidation(InvitedEmail);
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });
        _legalSubmission
            .Setup(s => s.ValidateAndStageAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<LegalAcceptanceItemInput>>(),
                true, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("At least one required legal document was not accepted.", 400));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("required legal document");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
