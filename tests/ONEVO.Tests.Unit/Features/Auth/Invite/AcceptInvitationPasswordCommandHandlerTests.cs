using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Invite;

/// <summary>
/// Covers the invitation-usability, tenant-scoping, legal-acceptance, and position-role
/// branches of AcceptInvitationPasswordCommandHandler that AcceptInvitationDirectoryTests
/// (directory-upsert focused) does not exercise.
/// </summary>
public class AcceptInvitationPasswordCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
    private const string RawToken = "raw-token-password-flow";
    private const string InvitedEmail = "invited@example.com";

    private readonly Mock<IInvitationTokenRepository> _invitations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ILegalAcceptanceSubmissionService> _legalSubmission = new();
    private readonly Mock<ILoginContinuationService> _continuation = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IGlobalEmailDirectoryRepository> _globalDirectory = new();
    private readonly Mock<IUserRoleRepository> _userRoles = new();
    private readonly Mock<IPositionRepository> _positions = new();

    public AcceptInvitationPasswordCommandHandlerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _tenantContext.Setup(t => t.IsResolved).Returns(true);
        _tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        _tenantContext.Setup(t => t.TenantId).Returns(TenantId);
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
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
        Guid? positionId = null) => new()
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
        RevokedAt = revokedAt
    };

    private AcceptInvitationPasswordCommandHandler BuildSut() => new(
        _invitations.Object, _users.Object, _passwordHasher.Object, _legalSubmission.Object,
        _continuation.Object, _unitOfWork.Object, _clock.Object, _tenantContext.Object,
        _globalDirectory.Object, _userRoles.Object, _positions.Object);

    private static AcceptInvitationPasswordCommand MakeCommand() => new(
        RawToken: RawToken,
        Password: "NewPassword123!",
        ConfirmPassword: "NewPassword123!",
        Acceptances: [],
        IpAddress: "127.0.0.1",
        UserAgent: "test-agent");

    [Fact]
    public async Task Handle_TenantContextNotTenantMode_ReturnsNotFound()
    {
        _tenantContext.Setup(t => t.ContextMode).Returns(TenantContextMode.Admin);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _invitations.Verify(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExpiredInvitation_ReturnsFailure400()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(expiresAt: Now.AddDays(-1)));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("expired");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RevokedInvitation_ReturnsFailure400()
    {
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
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(tenantId: Guid.NewGuid()));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_UserRecordMissing_ReturnsFailure500()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Handle_LegalAcceptanceValidationFails_PropagatesFailure_AndDoesNotPersist()
    {
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
        _globalDirectory.Verify(
            g => g.UpsertAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PositionWithDefaultRole_AssignsUserRole()
    {
        var positionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(positionId: positionId));
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });
        _positions.Setup(p => p.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, DefaultRoleId = roleId });

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userRoles.Verify(
            r => r.AddAsync(
                It.Is<UserRole>(ur => ur.UserId == UserId && ur.RoleId == roleId && ur.TenantId == TenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PositionWithoutDefaultRole_DoesNotAssignRole()
    {
        var positionId = Guid.NewGuid();
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(positionId: positionId));
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false });
        _positions.Setup(p => p.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, DefaultRoleId = null });

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userRoles.Verify(
            r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
