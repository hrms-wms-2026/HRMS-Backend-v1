using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;
using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class ApproveAccessGrantRequestCommandHandlerTests
{
    private readonly Mock<IAccessGrantRequestRepository> _accessGrantRequestRepository = new();
    private readonly Mock<IOnboardingDraftRepository> _draftRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserRoleRepository> _userRoleRepository = new();
    private readonly Mock<IPositionRepository> _positionRepository = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Mock<ILegalEntityRepository> _legalEntityRepository = new();
    private readonly Mock<IDepartmentRepository> _departmentRepository = new();
    private readonly Mock<IEmploymentTypeRepository> _employmentTypeRepository = new();
    private readonly Mock<IWorkModeRepository> _workModeRepository = new();
    private readonly Mock<ISeatEntitlementService> _seatEntitlementService = new();
    private readonly Mock<IChecklistTemplateRepository> _checklistTemplateRepository = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _checklistTaskRepository = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokenRepository = new();
    private readonly Mock<ITenantRepository> _tenantRepository = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<ISecureTokenGenerator> _tokenGenerator = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly Guid _templateId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    public ApproveAccessGrantRequestCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _currentUser.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        _legalEntityRepository
            .Setup(r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { IsActive = true });
        _departmentRepository
            .Setup(r => r.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { IsActive = true });
        _positionRepository
            .Setup(r => r.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = _positionId, TenantId = _tenantId, DepartmentId = _departmentId, IsActive = true, MaxOccupancy = 5 });
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAccessTemplate { Id = _templateId, TenantId = _tenantId, PositionId = _positionId, RoleId = _roleId, RequiresApproval = true, IsActive = true });

        _workModeRepository.Setup(r => r.ExistsActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _employmentTypeRepository.Setup(r => r.GetIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _positionAssignmentRepository
            .Setup(r => r.TryReservePositionAssignmentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Approved, 10, 0, 0, 10, false, false, "ok"));

        _userRepository
            .Setup(r => r.GetByTenantAndEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRoleRepository
            .Setup(r => r.ListActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserRole>());

        _tokenGenerator.Setup(t => t.GenerateUrlSafeOpaqueToken()).Returns("raw-token-value");

        _draftRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _tenantRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
    }

    private ApproveAccessGrantRequestCommandHandler CreateHandler() => new(
        _accessGrantRequestRepository.Object, _draftRepository.Object, _employeeRepository.Object, _userRepository.Object,
        _userRoleRepository.Object, _positionRepository.Object, _positionAssignmentRepository.Object,
        _legalEntityRepository.Object, _departmentRepository.Object, _employmentTypeRepository.Object,
        _workModeRepository.Object, _seatEntitlementService.Object, _checklistTemplateRepository.Object,
        _checklistTaskRepository.Object, _invitationTokenRepository.Object, _tenantRepository.Object, _outboxWriter.Object,
        _tokenGenerator.Object, _currentUser.Object, _clock.Object);

    private OnboardingDraftEntity ValidDraft(Guid draftId, string status = OnboardingDraftStatus.WaitingForPositionApproval) => new()
    {
        Id = draftId,
        TenantId = _tenantId,
        FirstName = "Ada",
        LastName = "Lovelace",
        WorkEmail = "ada@test.dev",
        LegalEntityId = _legalEntityId,
        DepartmentId = _departmentId,
        PositionId = _positionId,
        EmploymentType = "full_time",
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        EmployeeNumber = "EMP-001",
        WorkModeId = 1,
        Status = status,
        StartedById = _userId,
    };

    private AccessGrantRequest ValidGrantRequest(Guid requestId, Guid draftId, string status = "Pending") => new()
    {
        Id = requestId,
        TenantId = _tenantId,
        OnboardingDraftId = draftId,
        TargetPositionId = _positionId,
        TargetDepartmentId = _departmentId,
        PositionAccessTemplateId = _templateId,
        RequestedRoleId = _roleId,
        ApprovalStatus = status,
        RequestedByUserId = Guid.NewGuid(),
        RequestedAt = DateTimeOffset.UtcNow,
        EffectiveFrom = DateTimeOffset.UtcNow,
    };

    private void SetupHappyPath(out Guid requestId, out Guid draftId, string requestStatus = "Pending", string draftStatus = OnboardingDraftStatus.WaitingForPositionApproval)
    {
        var localRequestId = Guid.NewGuid();
        var localDraftId = Guid.NewGuid();
        var request = ValidGrantRequest(localRequestId, localDraftId, requestStatus);
        var draft = ValidDraft(localDraftId, draftStatus);
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, localRequestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, localDraftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        requestId = localRequestId;
        draftId = localDraftId;
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenRequestMissing()
    {
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessGrantRequest?)null);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenRequestAlreadyApproved()
    {
        SetupHappyPath(out var requestId, out var draftId, requestStatus: "Approved");

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenRequestAlreadyRejected()
    {
        SetupHappyPath(out var requestId, out var draftId, requestStatus: "Rejected");

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenRequestHasNoOnboardingDraftId()
    {
        var requestId = Guid.NewGuid();
        var request = new AccessGrantRequest { Id = requestId, TenantId = _tenantId, OnboardingDraftId = null, ApprovalStatus = "Pending" };
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftMissing()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var request = ValidGrantRequest(requestId, draftId);
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync((OnboardingDraftEntity?)null);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDraftNotWaitingForPositionApproval()
    {
        SetupHappyPath(out var requestId, out _, draftStatus: OnboardingDraftStatus.Draft);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDraftPositionNoLongerMatchesRequest()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var request = ValidGrantRequest(requestId, draftId);
        var draft = ValidDraft(draftId);
        draft.PositionId = Guid.NewGuid(); // moved since the request was submitted
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenAccessTemplateChangedSincePendingRequest()
    {
        SetupHappyPath(out var requestId, out _);
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = _positionId, RoleId = _roleId, RequiresApproval = true, IsActive = true });

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenRoleChangedSincePendingRequest()
    {
        SetupHappyPath(out var requestId, out _);
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAccessTemplate { Id = _templateId, TenantId = _tenantId, PositionId = _positionId, RoleId = Guid.NewGuid(), RequiresApproval = true, IsActive = true });

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDuplicateEmail()
    {
        SetupHappyPath(out var requestId, out _);
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDuplicateEmployeeNumber()
    {
        SetupHappyPath(out var requestId, out _);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SeatBlocked_CreatesNothingAndLeavesRequestPending()
    {
        SetupHappyPath(out var requestId, out _);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Blocked, 5, 5, 0, 0, false, true, "no seats"));

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SeatUndetermined_CreatesNothingAndDoesNotSave()
    {
        SetupHappyPath(out var requestId, out _);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Undetermined, null, 0, 0, null, false, false, "unconfigured"));

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SeatApproved_CreatesUserEmployeeAssignmentRoleTokenAndOutbox()
    {
        SetupHappyPath(out var requestId, out var draftId);

        User? addedUser = null;
        _userRepository.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => addedUser = u).Returns(Task.CompletedTask);
        EmployeeEntity? addedEmployee = null;
        _employeeRepository.Setup(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()))
            .Callback<EmployeeEntity, CancellationToken>((e, _) => addedEmployee = e).Returns(Task.CompletedTask);
        UserRole? addedRole = null;
        _userRoleRepository.Setup(r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .Callback<UserRole, CancellationToken>((r, _) => addedRole = r).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(addedUser);
        Assert.False(addedUser!.IsActive);
        Assert.True(addedUser.MustChangePassword);
        Assert.NotNull(addedEmployee);
        Assert.Equal(addedUser.Id, addedEmployee!.UserId);
        _positionAssignmentRepository.Verify(r => r.TryReservePositionAssignmentAsync(
            _tenantId, It.IsAny<Guid>(), _positionId, It.IsAny<DateOnly>(), _userId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(addedRole);
        Assert.Equal(_roleId, addedRole!.RoleId);
        Assert.Equal(_positionId, addedRole.SourcePositionId);
        Assert.Equal(_templateId, addedRole.SourcePositionAccessTemplateId);
        _invitationTokenRepository.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(w => w.EnqueueAsync(
            OutboxMessageTypes.EmployeeOnboardingInviteEmail,
            It.IsAny<EmployeeOnboardingInviteEmailPayload>(),
            _tenantId,
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(draftId, result.Value!.OnboardingDraftId);
        Assert.Equal("Approved", result.Value.PositionApprovalStatus);
        Assert.True(result.Value.InvitationQueued);
    }

    [Fact]
    public async Task Handle_WhenPositionAtCapacity_ReturnsConflict_AndDoesNotCreateEmployee()
    {
        SetupHappyPath(out var requestId, out _);
        _positionAssignmentRepository
            .Setup(r => r.TryReservePositionAssignmentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("This position has reached its capacity.", result.Error);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeExistsInDifferentLegalEntity_DoesNotBlock()
    {
        SetupHappyPath(out var requestId, out _);
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _employeeRepository.Verify(
            r => r.EmailExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeExistsInSameLegalEntity_ReturnsConflict()
    {
        SetupHappyPath(out var requestId, out _);
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ExistingUserAlreadyHoldingRequestedRole_SkipsDuplicateUserRoleInsert()
    {
        SetupHappyPath(out var requestId, out _);
        var existingUser = new User { Id = Guid.NewGuid(), TenantId = _tenantId, Email = "ada@test.dev", FirstName = "Ada", LastName = "Lovelace", IsActive = true };
        _userRepository
            .Setup(r => r.GetByTenantAndEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);
        _userRoleRepository
            .Setup(r => r.ListActiveByUserIdAsync(existingUser.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UserRole { TenantId = _tenantId, UserId = existingUser.Id, RoleId = _roleId } });

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _userRoleRepository.Verify(r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenSaveRacesOnUniqueConstraint()
    {
        SetupHappyPath(out var requestId, out _);
        _draftRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintConflictException(new Exception("dup")));

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenSaveRacesOnConcurrency()
    {
        SetupHappyPath(out var requestId, out _);
        _draftRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(new Exception("stale")));

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ChecklistTemplate_CreatesTasksAndReportsCount()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var request = ValidGrantRequest(requestId, draftId);
        var draft = ValidDraft(draftId);
        draft.SelectedTemplateId = templateId;
        _accessGrantRequestRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var template = new ChecklistTemplate { Id = templateId, TenantId = _tenantId, Name = "Starter", TemplateType = "onboarding", TasksJson = "[]" };
        _checklistTemplateRepository
            .Setup(r => r.GetActiveOnboardingAsync(_tenantId, templateId, _legalEntityId, _departmentId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _checklistTaskRepository
            .Setup(r => r.InstantiateAsync(template, It.IsAny<Guid>(), It.IsAny<Guid>(), null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new EmployeeChecklistTask(), new EmployeeChecklistTask() });

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.ChecklistTaskCount);
    }

    [Fact]
    public async Task Handle_CallerIsTheRequester_ReturnsForbidden()
    {
        var requestId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var grantRequest = ValidGrantRequest(requestId, draftId);
        grantRequest.RequestedByUserId = _userId;
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grantRequest);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(requestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot approve or reject a request you submitted yourself.", result.Error);
    }

    [Fact]
    public async Task Handle_PositionChangeActionType_ActivatesReservationAndEndsPreviousAssignment()
    {
        var reservedAssignmentId = Guid.NewGuid();
        var previousAssignmentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var grantRequest = ValidGrantRequest(Guid.NewGuid(), Guid.NewGuid());
        grantRequest.OnboardingDraftId = null;
        grantRequest.EmployeeId = employeeId;
        grantRequest.ActionType = AccessGrantActionType.PositionChange;
        grantRequest.ReservedPositionAssignmentId = reservedAssignmentId;
        grantRequest.ChangeReason = "Promotion";
        grantRequest.RequestedByUserId = Guid.NewGuid();
        _accessGrantRequestRepository
            .Setup(r => r.GetTrackedByIdAsync(_tenantId, grantRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grantRequest);
        _accessGrantRequestRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _positionAssignmentRepository
            .Setup(a => a.GetActivePrimaryAsync(_tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAssignmentEntity
            {
                Id = previousAssignmentId,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            });
        _positionAssignmentRepository
            .Setup(a => a.ActivatePlannedAsync(_tenantId, reservedAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new ApproveAccessGrantRequestCommand(grantRequest.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionAssignmentRepository.Verify(
            a => a.ActivatePlannedAsync(_tenantId, reservedAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        _positionAssignmentRepository.Verify(
            a => a.EndActiveAsync(_tenantId, previousAssignmentId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ConstructorDoesNotTakeATenantOwnerDependency()
    {
        var ctorParams = typeof(ApproveAccessGrantRequestCommandHandler)
            .GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(ctorParams, p => p.ParameterType.Name.Contains("TenantOwner", StringComparison.OrdinalIgnoreCase));
    }
}
