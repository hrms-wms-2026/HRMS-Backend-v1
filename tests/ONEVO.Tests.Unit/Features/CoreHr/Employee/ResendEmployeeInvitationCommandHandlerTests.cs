using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ResendEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class ResendEmployeeInvitationCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokenRepository = new();
    private readonly Mock<ITenantRepository> _tenantRepository = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<ISecureTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-15T12:00:00Z");

    public ResendEmployeeInvitationCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _tokenGenerator.Setup(t => t.GenerateUrlSafeOpaqueToken()).Returns("raw-token-value");
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, FirstName = "Ada", LastName = "Lovelace", Email = "ada@test.dev" });
        _tenantRepository
            .Setup(r => r.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
    }

    private ResendEmployeeInvitationCommandHandler CreateHandler() => new(
        _employeeRepository.Object, _invitationTokenRepository.Object, _tenantRepository.Object,
        _outboxWriter.Object, _tokenGenerator.Object, _unitOfWork.Object, _currentUser.Object, _clock.Object);

    private InvitationToken ExpiredUnacceptedInvitation() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        EmployeeId = _employeeId,
        UserId = Guid.NewGuid(),
        PositionId = Guid.NewGuid(),
        LegalEntityId = Guid.NewGuid(),
        OnboardingDraftId = Guid.NewGuid(),
        Purpose = InvitationToken.EmployeeOnboardingPurpose,
        ExpiresAt = _now.AddDays(-1),
        UsedAt = null,
        RevokedAt = null,
    };

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await CreateHandler().Handle(new ResendEmployeeInvitationCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoInvitationEverIssued_ReturnsFailure400()
    {
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);

        var result = await CreateHandler().Handle(new ResendEmployeeInvitationCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("No invitation", result.Error);
    }

    [Fact]
    public async Task Handle_InvitationAlreadyAccepted_ReturnsFailure400_AndDoesNotIssueNewToken()
    {
        var accepted = ExpiredUnacceptedInvitation();
        accepted.UsedAt = _now.AddHours(-1);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accepted);

        var result = await CreateHandler().Handle(new ResendEmployeeInvitationCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("already been accepted", result.Error);
        _invitationTokenRepository.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvitationRevoked_ReturnsFailure400()
    {
        var revoked = ExpiredUnacceptedInvitation();
        revoked.RevokedAt = _now.AddHours(-1);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revoked);

        var result = await CreateHandler().Handle(new ResendEmployeeInvitationCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("revoked", result.Error);
    }

    [Fact]
    public async Task Handle_InvitationNotYetExpired_ReturnsFailure400_AndDoesNotIssueNewToken()
    {
        var stillPending = ExpiredUnacceptedInvitation();
        stillPending.ExpiresAt = _now.AddDays(1);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stillPending);

        var result = await CreateHandler().Handle(new ResendEmployeeInvitationCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("not expired", result.Error);
        _invitationTokenRepository.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExpiredUnacceptedInvitation_RevokesOldTokenAndIssuesNewOne()
    {
        var expired = ExpiredUnacceptedInvitation();
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expired);

        var result = await CreateHandler().Handle(new ResendEmployeeInvitationCommand(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_now.AddHours(24), result.Value!.ExpiresAt);

        Assert.Equal(_now, expired.RevokedAt);
        Assert.Equal(_userId, expired.RevokedById);

        _invitationTokenRepository.Verify(r => r.AddAsync(
            It.Is<InvitationToken>(inv =>
                inv.EmployeeId == _employeeId
                && inv.TenantId == _tenantId
                && inv.UserId == expired.UserId
                && inv.PositionId == expired.PositionId
                && inv.OnboardingDraftId == expired.OnboardingDraftId
                && inv.Purpose == InvitationToken.EmployeeOnboardingPurpose
                && inv.InvitedEmail == "ada@test.dev"
                && inv.ExpiresAt == _now.AddHours(24)),
            It.IsAny<CancellationToken>()), Times.Once);

        _outboxWriter.Verify(w => w.EnqueueAsync(
            OutboxMessageTypes.EmployeeOnboardingInviteEmail,
            It.IsAny<EmployeeOnboardingInviteEmailPayload>(),
            _tenantId,
            It.IsAny<CancellationToken>()), Times.Once);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
