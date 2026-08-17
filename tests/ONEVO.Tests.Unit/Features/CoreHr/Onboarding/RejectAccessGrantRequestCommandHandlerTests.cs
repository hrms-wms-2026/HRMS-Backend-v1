using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class RejectAccessGrantRequestCommandHandlerTests
{
    private readonly Mock<IAccessGrantRequestRepository> _accessGrantRequestRepository = new();
    private readonly Mock<IOnboardingDraftRepository> _draftRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public RejectAccessGrantRequestCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _draftRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private RejectAccessGrantRequestCommandHandler CreateHandler() => new(
        _accessGrantRequestRepository.Object, _draftRepository.Object, _currentUser.Object, _clock.Object);

    private AccessGrantRequest ValidGrantRequest(Guid requestId, Guid? draftId, string status = "Pending") => new()
    {
        Id = requestId,
        TenantId = _tenantId,
        OnboardingDraftId = draftId,
        TargetPositionId = Guid.NewGuid(),
        TargetDepartmentId = Guid.NewGuid(),
        PositionAccessTemplateId = Guid.NewGuid(),
        RequestedRoleId = Guid.NewGuid(),
        ApprovalStatus = status,
        RequestedByUserId = Guid.NewGuid(),
        RequestedAt = DateTimeOffset.UtcNow,
        EffectiveFrom = DateTimeOffset.UtcNow,
    };

    private OnboardingDraftEntity ValidDraft(Guid draftId) => new()
    {
        Id = draftId,
        TenantId = _tenantId,
        FirstName = "Ada",
        LastName = "Lovelace",
        WorkEmail = "ada@test.dev",
        LegalEntityId = Guid.NewGuid(),
        EmploymentType = "full_time",
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        WorkModeId = 1,
        Status = OnboardingDraftStatus.WaitingForPositionApproval,
        DraftReason = OnboardingDraftReason.WaitingForPositionApproval,
        StartedById = _userId,
    };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenRequestMissing()
    {
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessGrantRequest?)null);

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenRequestAlreadyApproved()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGrantRequest(requestId, draftId, "Approved"));

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, "no longer needed"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenRequestAlreadyRejected()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGrantRequest(requestId, draftId, "Rejected"));

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenDecisionNoteTooLong()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGrantRequest(requestId, draftId));

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, new string('x', 501)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenRequestHasNoOnboardingDraftId()
    {
        var requestId = Guid.NewGuid();
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGrantRequest(requestId, draftId: null));

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftMissing()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidGrantRequest(requestId, draftId));
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync((OnboardingDraftEntity?)null);

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PendingRequest_MarksRejectedAndMovesDraftToRetryableState()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var request = ValidGrantRequest(requestId, draftId);
        var draft = ValidDraft(draftId);
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, "Position filled internally"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", request.ApprovalStatus);
        Assert.Equal(_userId, request.DecidedByUserId);
        Assert.Equal("Position filled internally", request.DecisionNote);
        Assert.Equal(OnboardingDraftStatus.Draft, draft.Status);
        Assert.Equal(OnboardingDraftReason.PositionApprovalRejected, draft.DraftReason);
        Assert.Equal("Rejected", result.Value!.RequestStatus);
        Assert.Equal(OnboardingDraftStatus.Draft, result.Value.DraftStatus);
        Assert.Equal(OnboardingDraftReason.PositionApprovalRejected, result.Value.DraftReason);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PendingRequest_WhenDraftAlreadyMovedOn_LeavesDraftStatusUnchanged()
    {
        // Defensive guard: a stale Pending request rejected against a draft that has since been
        // decided another way (e.g. Finalized via a different request) must not silently undo
        // that outcome by resetting it back to Draft.
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var request = ValidGrantRequest(requestId, draftId);
        var draft = ValidDraft(draftId);
        draft.Status = OnboardingDraftStatus.Finalized;
        draft.DraftReason = OnboardingDraftReason.InvitationSent;
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", request.ApprovalStatus);
        Assert.Equal(OnboardingDraftStatus.Finalized, draft.Status);
        Assert.Equal(OnboardingDraftReason.InvitationSent, draft.DraftReason);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenSaveRacesOnConcurrency()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(ValidGrantRequest(requestId, draftId));
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync(ValidDraft(draftId));
        _draftRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(new Exception("stale")));

        var result = await CreateHandler().Handle(new RejectAccessGrantRequestCommand(requestId, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
