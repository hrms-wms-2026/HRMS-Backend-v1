using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

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
    private readonly Mock<IPositionAssignmentRepository> _positionAssignments = new();
    private readonly Mock<ILegalEntityRepository> _legalEntityRepository = new();
    private readonly Mock<IDepartmentRepository> _departmentRepository = new();
    private readonly Mock<ISeatEntitlementService> _seatEntitlementService = new();
    private readonly Mock<IWorkModeRepository> _workModeRepository = new();
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
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Undetermined, null, 0, 0, null, false, true, "no source"));
        _workModeRepository.Setup(r => r.ExistsActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _legalEntityRepository.Setup(r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LegalEntity { IsActive = true });
        _positionRepository.Setup(r => r.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Position { IsActive = true });
        _draftRepository
            .Setup(r => r.GetResponseByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid id, CancellationToken _) =>
                new OnboardingDraftResponse(id, "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
                    null, null, "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null,
                    null, OnboardingDraftStatus.WaitingForSeat, OnboardingDraftReason.WaitingForSeat,
                    OnboardingWizardStep.EmployeeDetails, _userId, "1", null, null, null));
    }

    private SaveOnboardingDraftCommandHandler CreateHandler() => new(CreateWriteService(), _currentUser.Object);

    private OnboardingDraftWriteService CreateWriteService() => new(
        _draftRepository.Object, _employeeRepository.Object,
        Mock.Of<IUserRepository>(), Mock.Of<IUserRoleRepository>(),
        _positionRepository.Object, _positionAssignments.Object,
        _legalEntityRepository.Object, _departmentRepository.Object,
        Mock.Of<IEmploymentTypeRepository>(), _workModeRepository.Object,
        _seatEntitlementService.Object, Mock.Of<IAccessGrantRequestRepository>(),
        Mock.Of<IPermissionRepository>(), Mock.Of<IChecklistTemplateRepository>(),
        Mock.Of<IEmployeeChecklistTaskRepository>(), Mock.Of<IInvitationTokenRepository>(),
        Mock.Of<ITenantRepository>(), Mock.Of<IOutboxWriter>(),
        Mock.Of<ISecureTokenGenerator>(), _currentUser.Object, _clock.Object, Mock.Of<IUnitOfWork>());

    private SaveOnboardingDraftCommand ValidCommand(Guid? draftId = null, Guid? positionId = null, string? ifMatch = null) => new(
        draftId, "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, positionId,
        "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null,
        OnboardingWizardStep.EmployeeDetails, ifMatch, null);

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
    public async Task Handle_SavesDraftWithSeatConfigurationRequired_WhenSeatDecisionIsUndetermined()
    {
        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(OnboardingDraftStatus.Draft, added!.Status);
        Assert.Equal(OnboardingDraftReason.SeatConfigurationRequired, added.DraftReason);
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
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(_tenantId, It.IsAny<Guid>(), "ada@test.dev", null, It.IsAny<CancellationToken>()))
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
            null, "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
            "full_time", DateOnly.FromDateTime(DateTime.UtcNow), "E-001", 1, null, null,
            OnboardingWizardStep.EmployeeDetails, null, null);
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
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Approved, 10, 3, 0, 7, false, false, "ok"));
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

    [Fact]
    public async Task Handle_EmployeeExistsInDifferentLegalEntity_DoesNotBlock()
    {
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _employeeRepository.Verify(
            r => r.EmailExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeExistsInSameLegalEntity_ReturnsConflict()
    {
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task SaveAsync_Requires_ReportsToEmployeeId_When_Position_Target_Is_Pooled()
    {
        var positionId = Guid.NewGuid();
        var pooledTargetId = Guid.NewGuid();
        _positionRepository.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, IsActive = true, ReportsToPositionId = pooledTargetId });
        _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
            });

        var result = await CreateWriteService().SaveAsync(_tenantId, _userId, ValidCommand(positionId: positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task SaveAsync_Rejects_ReportsToEmployeeId_Not_A_Current_Active_Holder()
    {
        var positionId = Guid.NewGuid();
        var pooledTargetId = Guid.NewGuid();
        _positionRepository.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, IsActive = true, ReportsToPositionId = pooledTargetId });
        _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
            });

        var command = ValidCommand(positionId: positionId) with { ReportsToEmployeeId = Guid.NewGuid() };
        var result = await CreateWriteService().SaveAsync(_tenantId, _userId, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task SaveAsync_Persists_ReportsToEmployeeId_When_Valid()
    {
        var positionId = Guid.NewGuid();
        var pooledTargetId = Guid.NewGuid();
        var chosenManagerId = Guid.NewGuid();
        _positionRepository.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, IsActive = true, ReportsToPositionId = pooledTargetId });
        _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder> { new(chosenManagerId, "A", "One", "a@acme.test", null) });

        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        var command = ValidCommand(positionId: positionId) with { ReportsToEmployeeId = chosenManagerId };
        var result = await CreateWriteService().SaveAsync(_tenantId, _userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(chosenManagerId, added!.ReportsToEmployeeId);
    }

    [Fact]
    public async Task SaveAsync_Ignores_ReportsToEmployeeId_When_Position_Target_Has_Single_Holder()
    {
        var positionId = Guid.NewGuid();
        var uniqueTargetId = Guid.NewGuid();
        _positionRepository.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, IsActive = true, ReportsToPositionId = uniqueTargetId });
        _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, uniqueTargetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "A", "One", "a@acme.test", null) });

        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        var result = await CreateWriteService().SaveAsync(_tenantId, _userId, ValidCommand(positionId: positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Null(added!.ReportsToEmployeeId);
    }

    [Fact]
    public async Task Handle_AcceptsEmployeeEditedTaskWithoutAssignedToId()
    {
        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        var json = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueDate\":\"2026-09-01\",\"sequence\":1,\"isRequired\":true}]";
        var command = ValidCommand() with { EditedTasksJson = json, SelectedTemplateId = Guid.NewGuid() };

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(json, added!.EditedTasksJson);
    }

    [Fact]
    public async Task Handle_RejectsAnotherPersonEditedTaskWithoutAssignedToId()
    {
        var json = $"[{{\"title\":\"IT setup\",\"ownerType\":\"custom_user\",\"assigneePositionId\":\"{Guid.NewGuid()}\",\"dueDate\":\"2026-09-01\",\"sequence\":1,\"isRequired\":true}}]";
        var command = ValidCommand() with { EditedTasksJson = json, SelectedTemplateId = Guid.NewGuid() };

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        _draftRepository.Verify(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
