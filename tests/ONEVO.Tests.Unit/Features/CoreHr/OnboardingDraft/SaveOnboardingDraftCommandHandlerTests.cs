using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

// Namespace deliberately avoids a bare ".OnboardingDraft" segment: it would collide with the
// OnboardingDraft entity type via ancestor-namespace lookup for any sibling test file in
// ONEVO.Tests.Unit.Features.CoreHr that imports ONEVO.Domain.Features.CoreHr.Entities (same
// convention documented in EfDepartmentRepository's namespace comment).
namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class SaveOnboardingDraftCommandHandlerTests
{
    private readonly Mock<IOnboardingDraftRepository> _draftRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IPositionRepository> _positionRepository = new();
    private readonly Mock<ISeatEntitlementService> _seatEntitlementService = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public SaveOnboardingDraftCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _employeeRepository
            .Setup(r => r.EmailExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Undetermined, null, 0, 0, null, false, true, "no source"));
        _draftRepository
            .Setup(r => r.GetResponseByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid id, CancellationToken _) =>
                new OnboardingDraftResponse(id, "Ada Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
                    "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null,
                    OnboardingDraftStatus.WaitingForSeat, OnboardingDraftReason.WaitingForSeat,
                    OnboardingWizardStep.EmployeeDetails, _userId, "1"));
    }

    private SaveOnboardingDraftCommandHandler CreateHandler() => new(
        _draftRepository.Object, _employeeRepository.Object, _positionRepository.Object,
        _seatEntitlementService.Object, _currentUser.Object, _clock.Object);

    private SaveOnboardingDraftCommand ValidCommand(Guid? draftId = null, Guid? positionId = null, string? ifMatch = null) => new(
        draftId, "Ada Lovelace", "ada@test.dev", Guid.NewGuid(), null, positionId,
        "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null,
        OnboardingWizardStep.EmployeeDetails, ifMatch);

    [Fact]
    public async Task Handle_SetsWaitingForPositionApproval_WhenSelectedPositionRequiresApproval()
    {
        var positionId = Guid.NewGuid();
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = positionId, RoleId = Guid.NewGuid(), RequiresApproval = true, IsActive = true });

        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(ValidCommand(positionId: positionId), CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(OnboardingDraftStatus.WaitingForPositionApproval, added!.Status);
        Assert.Equal(OnboardingDraftReason.WaitingForPositionApproval, added.DraftReason);
        _seatEntitlementService.Verify(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SetsWaitingForSeat_WhenNoApprovalRequiredAndSeatDecisionIsUndetermined()
    {
        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(OnboardingDraftStatus.WaitingForSeat, added!.Status);
        Assert.Equal(OnboardingDraftReason.WaitingForSeat, added.DraftReason);
    }

    [Fact]
    public async Task Handle_SetsSavedManually_WhenSeatDecisionIsApproved()
    {
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Approved, 10, 3, 0, 7, false, false, "ok"));

        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(OnboardingDraftStatus.Draft, added!.Status);
        Assert.Equal(OnboardingDraftReason.SavedManually, added.DraftReason);
    }

    [Fact]
    public async Task Handle_NeverCallsAnyUserOrOutboxRelatedRepository()
    {
        // There is no user-account or invitation dependency injected into this handler at all -
        // this test documents that invariant so a future edit can't silently add one without
        // this test forcing a conscious change to the constructor signature.
        var handler = CreateHandler();
        var ctorParams = typeof(SaveOnboardingDraftCommandHandler)
            .GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(ctorParams, p =>
            p.ParameterType.Name.Contains("User", StringComparison.OrdinalIgnoreCase) && p.ParameterType.Name != nameof(ICurrentUser));
        Assert.DoesNotContain(ctorParams, p => p.ParameterType.Name.Contains("Outbox", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ctorParams, p => p.ParameterType.Name.Contains("Invit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenWorkEmailAlreadyBelongsToAnExistingEmployee()
    {
        _employeeRepository
            .Setup(r => r.EmailExistsAsync(_tenantId, "ada@test.dev", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _draftRepository.Verify(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenEmployeeNumberAlreadyInUse()
    {
        var command = new SaveOnboardingDraftCommand(
            null, "Ada Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
            "full_time", DateOnly.FromDateTime(DateTime.UtcNow), "E-001", null, null, null,
            OnboardingWizardStep.EmployeeDetails, null);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(_tenantId, "E-001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ResumesAnExistingDraftById_AndPreservesStartedById()
    {
        var draftId = Guid.NewGuid();
        var originalStarter = Guid.NewGuid();
        var existing = new OnboardingDraftEntity { Id = draftId, TenantId = _tenantId, StartedById = originalStarter };
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _currentUser.Setup(u => u.HasPermission("employees:write")).Returns(true);

        await CreateHandler().Handle(ValidCommand(draftId: draftId), CancellationToken.None);

        Assert.Equal(originalStarter, existing.StartedById);
        _draftRepository.Verify(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenADifferentUsersDraftIsEditedWithoutEmployeesWrite()
    {
        var draftId = Guid.NewGuid();
        var existing = new OnboardingDraftEntity { Id = draftId, TenantId = _tenantId, StartedById = Guid.NewGuid() };
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _currentUser.Setup(u => u.HasPermission("employees:write")).Returns(false);

        var result = await CreateHandler().Handle(ValidCommand(draftId: draftId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftIdDoesNotExist()
    {
        var draftId = Guid.NewGuid();
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OnboardingDraftEntity?)null);

        var result = await CreateHandler().Handle(ValidCommand(draftId: draftId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenSaveChangesThrowsConcurrencyConflictException()
    {
        _draftRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException());

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SetsExpectedVersion_WhenIfMatchProvidedOnUpdate()
    {
        var draftId = Guid.NewGuid();
        var existing = new OnboardingDraftEntity { Id = draftId, TenantId = _tenantId, StartedById = _userId };
        _draftRepository
            .Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await CreateHandler().Handle(ValidCommand(draftId: draftId, ifMatch: "42"), CancellationToken.None);

        _draftRepository.Verify(r => r.SetExpectedVersion(existing, "42"), Times.Once);
    }
}
