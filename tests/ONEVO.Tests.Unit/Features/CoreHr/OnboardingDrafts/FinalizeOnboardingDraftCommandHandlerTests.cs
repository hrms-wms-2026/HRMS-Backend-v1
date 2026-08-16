using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.FinalizeOnboardingDraft;
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

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class FinalizeOnboardingDraftCommandHandlerTests
{
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
    private readonly Mock<IAccessGrantRequestRepository> _accessGrantRequestRepository = new();
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

    public FinalizeOnboardingDraftCommandHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _currentUser.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        _legalEntityRepository
            .Setup(r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { IsActive = true });

        _workModeRepository.Setup(r => r.ExistsActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _employmentTypeRepository.Setup(r => r.GetIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _employeeRepository
            .Setup(r => r.EmailExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _positionAssignmentRepository
            .Setup(r => r.CountActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Approved, 10, 0, 0, 10, false, false, "ok"));

        _accessGrantRequestRepository
            .Setup(r => r.GetPendingByDraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessGrantRequest?)null);

        _userRepository
            .Setup(r => r.GetByTenantAndEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _tokenGenerator.Setup(t => t.GenerateUrlSafeOpaqueToken()).Returns("raw-token-value");

        _tenantRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });

        _draftRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private FinalizeOnboardingDraftCommandHandler CreateHandler() => new(
        _draftRepository.Object, _employeeRepository.Object, _userRepository.Object, _userRoleRepository.Object,
        _positionRepository.Object, _positionAssignmentRepository.Object, _legalEntityRepository.Object,
        _departmentRepository.Object, _employmentTypeRepository.Object, _workModeRepository.Object,
        _seatEntitlementService.Object, _accessGrantRequestRepository.Object, _checklistTemplateRepository.Object,
        _checklistTaskRepository.Object, _invitationTokenRepository.Object, _tenantRepository.Object, _outboxWriter.Object,
        _tokenGenerator.Object, _currentUser.Object, _clock.Object);

    private OnboardingDraftEntity ValidDraft(
        Guid? id = null, string status = OnboardingDraftStatus.Draft, Guid? positionId = null,
        Guid? departmentId = null, Guid? selectedTemplateId = null, string? editedTasksJson = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = _tenantId,
        FirstName = "Ada",
        LastName = "Lovelace",
        WorkEmail = "ada@test.dev",
        LegalEntityId = _legalEntityId,
        DepartmentId = departmentId,
        PositionId = positionId,
        EmploymentType = "full_time",
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        EmployeeNumber = "EMP-001",
        WorkModeId = 1,
        SelectedTemplateId = selectedTemplateId,
        EditedTasksJson = editedTasksJson,
        Status = status,
        StartedById = _userId,
    };

    private void SetupDraft(OnboardingDraftEntity draft) =>
        _draftRepository.Setup(r => r.GetTrackedAsync(_tenantId, draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftMissing()
    {
        _draftRepository
            .Setup(r => r.GetTrackedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OnboardingDraftEntity?)null);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDraftAlreadyFinalized()
    {
        var draft = ValidDraft(status: OnboardingDraftStatus.Finalized);
        SetupDraft(draft);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDraftCancelled()
    {
        var draft = ValidDraft(status: OnboardingDraftStatus.Cancelled);
        SetupDraft(draft);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenDraftIsWaitingForPositionApprovalAndRequestStillPending()
    {
        var draft = ValidDraft(status: OnboardingDraftStatus.WaitingForPositionApproval);
        SetupDraft(draft);
        _accessGrantRequestRepository
            .Setup(r => r.AnyPendingByDraftAsync(_tenantId, draft.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DraftWaitingForApprovalWithNoPendingRequest_CreatesFreshAccessGrantRequest()
    {
        // Simulates the post-rejection retry path: RejectAccessGrantRequestCommandHandler moved
        // the draft back to Draft, but HR re-saved via PUT without changing the (still
        // approval-requiring) position, so SaveOnboardingDraftCommandHandler re-stamped
        // WaitingForPositionApproval even though the old request is Rejected, not Pending.
        // Finalize must not 409 here - it must re-evaluate and submit a fresh request.
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId, status: OnboardingDraftStatus.WaitingForPositionApproval);
        SetupDraft(draft);
        SetupPosition(positionId, departmentId: Guid.NewGuid());
        var template = new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = positionId, RoleId = Guid.NewGuid(), RequiresApproval = true, IsActive = true };
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _accessGrantRequestRepository
            .Setup(r => r.AnyPendingByDraftAsync(_tenantId, draft.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // GetPendingByDraftAsync (constructor default) returns null - the old request is
        // Rejected, not Pending, so it is never matched or reused.

        AccessGrantRequest? addedRequest = null;
        _accessGrantRequestRepository.Setup(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AccessGrantRequest, CancellationToken>((r, _) => addedRequest = r).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(addedRequest);
        Assert.Equal("Pending", addedRequest!.ApprovalStatus);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(OnboardingDraftStatus.WaitingForPositionApproval, draft.Status);
        Assert.Equal(OnboardingDraftReason.WaitingForPositionApproval, draft.DraftReason);
    }

    [Fact]
    public async Task Handle_DraftWaitingForApprovalWithNoPendingRequest_FinalizesImmediately_WhenPositionNoLongerRequiresApproval()
    {
        // HR changed the position (or its access template) after a rejection so it no longer
        // requires approval - finalize must validate against current rules, not the stale status.
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId, status: OnboardingDraftStatus.WaitingForPositionApproval);
        SetupDraft(draft);
        SetupPosition(positionId, departmentId: Guid.NewGuid());
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionAccessTemplate?)null);
        _accessGrantRequestRepository
            .Setup(r => r.AnyPendingByDraftAsync(_tenantId, draft.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnboardingDraftStatus.Finalized, draft.Status);
        Assert.True(result.Value!.InvitationQueued);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenLegalEntityInvalid()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _legalEntityRepository
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, draft.LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenDepartmentBelongsToAnotherLegalEntity()
    {
        var departmentId = Guid.NewGuid();
        var draft = ValidDraft(departmentId: departmentId);
        SetupDraft(draft);
        // Scoped lookup by (tenant, legalEntityId, departmentId) naturally returns null when the
        // department belongs to a different legal entity than the one on the draft.
        _departmentRepository
            .Setup(r => r.GetByIdForLegalEntityAsync(_tenantId, draft.LegalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenPositionBelongsToAnotherLegalEntity()
    {
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId);
        SetupDraft(draft);
        _positionRepository
            .Setup(r => r.GetByIdForLegalEntityAsync(_tenantId, draft.LegalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenWorkModeInactive()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _workModeRepository.Setup(r => r.ExistsActiveAsync(draft.WorkModeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenEmployeeEmailAlreadyExists()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _employeeRepository
            .Setup(r => r.EmailExistsAsync(_tenantId, draft.WorkEmail, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenEmployeeNumberAlreadyExists()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(_tenantId, draft.EmployeeNumber!, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CreatesPendingUserEmployeeTokenAndOutbox_WhenSeatApproved()
    {
        var draft = ValidDraft();
        SetupDraft(draft);

        User? addedUser = null;
        _userRepository.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => addedUser = u).Returns(Task.CompletedTask);
        EmployeeEntity? addedEmployee = null;
        _employeeRepository.Setup(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()))
            .Callback<EmployeeEntity, CancellationToken>((e, _) => addedEmployee = e).Returns(Task.CompletedTask);
        InvitationToken? addedToken = null;
        _invitationTokenRepository.Setup(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()))
            .Callback<InvitationToken, CancellationToken>((t, _) => addedToken = t).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(addedUser);
        Assert.False(addedUser!.IsActive);
        Assert.NotNull(addedEmployee);
        Assert.Equal(addedUser.Id, addedEmployee!.UserId);
        Assert.NotNull(addedToken);
        Assert.Equal(InvitationToken.EmployeeOnboardingPurpose, addedToken!.Purpose);
        Assert.Equal(draft.Id, addedToken.OnboardingDraftId);
        _outboxWriter.Verify(w => w.EnqueueAsync(
            OutboxMessageTypes.EmployeeOnboardingInviteEmail, It.IsAny<EmployeeOnboardingInviteEmailPayload>(), _tenantId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(OnboardingDraftStatus.Finalized, draft.Status);
        Assert.Equal(OnboardingDraftReason.InvitationSent, draft.DraftReason);
        Assert.NotNull(draft.FinalizedAt);
        Assert.True(result.Value!.InvitationQueued);
        Assert.False(result.Value.PositionApprovalPending);
    }

    [Fact]
    public async Task Handle_EnqueuesInviteEmail_WithTenantSlugAnd24HourExpiry()
    {
        var draft = ValidDraft();
        SetupDraft(draft);

        EmployeeOnboardingInviteEmailPayload? payload = null;
        _outboxWriter.Setup(w => w.EnqueueAsync(
                OutboxMessageTypes.EmployeeOnboardingInviteEmail, It.IsAny<EmployeeOnboardingInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, Guid?, CancellationToken>((_, p, _, _) => payload = (EmployeeOnboardingInviteEmailPayload)p)
            .Returns(Task.CompletedTask);

        var before = _clock.Object.UtcNow;
        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(payload);
        Assert.Equal("acme", payload!.TenantSlug);
        Assert.Equal(before.AddHours(24), payload.ExpiresAt);
        _tenantRepository.Verify(r => r.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReusesExistingTenantUser_InsteadOfCreatingDuplicate()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        var existingUser = new User { Id = Guid.NewGuid(), TenantId = _tenantId, Email = draft.WorkEmail, IsActive = false };
        _userRepository
            .Setup(r => r.GetByTenantAndEmailAsync(_tenantId, draft.WorkEmail.Trim().ToLowerInvariant(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreatesNothing_WhenSeatBlocked()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Blocked, 5, 5, 0, 0, false, true, "no seats"));

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _invitationTokenRepository.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriter.Verify(w => w.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<EmployeeOnboardingInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(OnboardingDraftStatus.WaitingForSeat, draft.Status);
        Assert.Equal(OnboardingDraftReason.WaitingForSeat, draft.DraftReason);
    }

    [Fact]
    public async Task Handle_CreatesNothing_WhenSeatUndetermined()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Undetermined, null, 0, 0, null, false, true, "no source"));

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoAccessTemplate_CreatesNoUserRole()
    {
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId);
        SetupDraft(draft);
        SetupPosition(positionId, departmentId: Guid.NewGuid());
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionAccessTemplate?)null);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _userRoleRepository.Verify(r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AccessTemplateWithoutApproval_CreatesUserRoleWithSourceFields()
    {
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId);
        SetupDraft(draft);
        SetupPosition(positionId, departmentId: Guid.NewGuid());
        var roleId = Guid.NewGuid();
        var template = new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = positionId, RoleId = roleId, RequiresApproval = false, IsActive = true };
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        UserRole? addedRole = null;
        _userRoleRepository.Setup(r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .Callback<UserRole, CancellationToken>((r, _) => addedRole = r).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(addedRole);
        Assert.Equal(roleId, addedRole!.RoleId);
        Assert.Equal(positionId, addedRole.SourcePositionId);
        Assert.Equal(template.Id, addedRole.SourcePositionAccessTemplateId);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AccessTemplateRequiringApproval_CreatesAccessGrantRequestAndDefersEverythingElse()
    {
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId);
        SetupDraft(draft);
        SetupPosition(positionId, departmentId: Guid.NewGuid());
        var template = new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = positionId, RoleId = Guid.NewGuid(), RequiresApproval = true, IsActive = true };
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        AccessGrantRequest? addedRequest = null;
        _accessGrantRequestRepository.Setup(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AccessGrantRequest, CancellationToken>((r, _) => addedRequest = r).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(addedRequest);
        Assert.Null(addedRequest!.EmployeeId);
        Assert.Null(addedRequest.UserId);
        Assert.Equal(draft.Id, addedRequest.OnboardingDraftId);
        Assert.Equal("Pending", addedRequest.ApprovalStatus);

        // Per the userflow doc's Sensitive Position Approval rules: nothing else is created yet.
        _userRoleRepository.Verify(r => r.AddAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _positionAssignmentRepository.Verify(r => r.AddAsync(It.IsAny<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        _invitationTokenRepository.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriter.Verify(w => w.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<EmployeeOnboardingInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _checklistTaskRepository.Verify(r => r.InstantiateAsync(
            It.IsAny<ChecklistTemplate>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _seatEntitlementService.Verify(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(OnboardingDraftStatus.WaitingForPositionApproval, draft.Status);
        Assert.Equal(OnboardingDraftReason.WaitingForPositionApproval, draft.DraftReason);
        Assert.Null(draft.FinalizedAt);
        Assert.True(result.Value!.PositionApprovalPending);
        Assert.False(result.Value.InvitationQueued);
        Assert.Null(result.Value.EmployeeId);
    }

    [Fact]
    public async Task Handle_RepeatedFinalizeWhilePendingApproval_DoesNotDuplicateAccessGrantRequest()
    {
        var positionId = Guid.NewGuid();
        var draft = ValidDraft(positionId: positionId, status: OnboardingDraftStatus.WaitingForSeat);
        SetupDraft(draft);
        SetupPosition(positionId, departmentId: Guid.NewGuid());
        var template = new PositionAccessTemplate { Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = positionId, RoleId = Guid.NewGuid(), RequiresApproval = true, IsActive = true };
        _positionRepository
            .Setup(r => r.GetAccessTemplateByPositionAsync(_tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        // A pending request already exists for this draft/position/template.
        _accessGrantRequestRepository
            .Setup(r => r.GetPendingByDraftAsync(_tenantId, draft.Id, positionId, template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessGrantRequest { Id = Guid.NewGuid(), TenantId = _tenantId, OnboardingDraftId = draft.Id, ApprovalStatus = "Pending" });

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _accessGrantRequestRepository.Verify(r => r.AddAsync(It.IsAny<AccessGrantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SelectedChecklistTemplate_CreatesEmployeeChecklistTasks()
    {
        var templateId = Guid.NewGuid();
        var draft = ValidDraft(selectedTemplateId: templateId, editedTasksJson: "[{\"edited\":true}]");
        SetupDraft(draft);
        var template = new ChecklistTemplate { Id = templateId, TenantId = _tenantId, TemplateType = "onboarding", TasksJson = "[{\"template\":true}]", IsActive = true };
        _checklistTemplateRepository
            .Setup(r => r.GetActiveOnboardingAsync(_tenantId, templateId, draft.LegalEntityId, draft.DepartmentId, draft.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _checklistTaskRepository
            .Setup(r => r.InstantiateAsync(template, It.IsAny<Guid>(), It.IsAny<Guid>(), draft.EditedTasksJson, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeChecklistTask> { new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() } });

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ChecklistTasksCreated);
        // Confirms the edited draft JSON (not the template's own TasksJson) is what gets passed through.
        _checklistTaskRepository.Verify(r => r.InstantiateAsync(template, It.IsAny<Guid>(), It.IsAny<Guid>(), "[{\"edited\":true}]", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SelectedTemplateNoLongerAppliesToDraftPosition_ReturnsUnprocessableEntity()
    {
        var draft = ValidDraft(selectedTemplateId: Guid.NewGuid());
        SetupDraft(draft);
        _checklistTemplateRepository
            .Setup(r => r.GetActiveOnboardingAsync(_tenantId, draft.SelectedTemplateId!.Value, draft.LegalEntityId, draft.DepartmentId, draft.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChecklistTemplate?)null);

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        _checklistTaskRepository.Verify(r => r.InstantiateAsync(
            It.IsAny<ChecklistTemplate>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TemplateTaskOwnedByEmployee_ResolvesAssignedToIdToTheNewHiresOwnUserId()
    {
        var templateId = Guid.NewGuid();
        var draft = ValidDraft(selectedTemplateId: templateId);
        SetupDraft(draft);
        var template = new ChecklistTemplate
        {
            Id = templateId, TenantId = _tenantId, TemplateType = "onboarding", IsActive = true, LegalEntityId = draft.LegalEntityId,
            TasksJson = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true}]",
        };
        _checklistTemplateRepository
            .Setup(r => r.GetActiveOnboardingAsync(_tenantId, templateId, draft.LegalEntityId, draft.DepartmentId, draft.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _checklistTaskRepository
            .Setup(r => r.InstantiateAsync(template, It.IsAny<Guid>(), It.IsAny<Guid>(), null, draft.StartDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeChecklistTask> { new() { Id = Guid.NewGuid(), AssignedToId = Guid.NewGuid() } });

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _checklistTaskRepository.Verify(r => r.InstantiateAsync(template, It.IsAny<Guid>(), It.IsAny<Guid>(), null, draft.StartDate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingAssignedToId_RejectsBeforeCreatingAnything()
    {
        var templateId = Guid.NewGuid();
        var draft = ValidDraft(selectedTemplateId: templateId);
        SetupDraft(draft);
        var template = new ChecklistTemplate { Id = templateId, TenantId = _tenantId, TemplateType = "onboarding", TasksJson = "[{\"title\":\"x\"}]", IsActive = true };
        _checklistTemplateRepository
            .Setup(r => r.GetActiveOnboardingAsync(_tenantId, templateId, draft.LegalEntityId, draft.DepartmentId, draft.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _checklistTaskRepository
            .Setup(r => r.InstantiateAsync(template, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Each checklist task requires title, ownerType, assignedToId, and dueDate."));

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        // The new-hire user may be staged (AddAsync called) before checklist instantiation runs -
        // see FinalizeOnboardingDraftCommandHandler's reorder comment - but nothing is ever
        // persisted, which is what _draftRepository.SaveChangesAsync below actually guarantees.
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<EmployeeEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _invitationTokenRepository.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriter.Verify(w => w.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<EmployeeOnboardingInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _draftRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UsesEmployeeOnboardingTokenPurpose_NotGeneralPurpose()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        InvitationToken? addedToken = null;
        _invitationTokenRepository.Setup(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()))
            .Callback<InvitationToken, CancellationToken>((t, _) => addedToken = t).Returns(Task.CompletedTask);

        await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.Equal("employee_onboarding", addedToken!.Purpose);
        Assert.NotEqual("general", addedToken.Purpose);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenSaveRacesOnUniqueConstraint()
    {
        var draft = ValidDraft();
        SetupDraft(draft);
        _draftRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintConflictException(new Exception("duplicate key value violates unique constraint")));

        var result = await CreateHandler().Handle(new FinalizeOnboardingDraftCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DoesNotDependOnTenantOwnerProvisioningService()
    {
        var ctorParams = typeof(FinalizeOnboardingDraftCommandHandler)
            .GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(ctorParams, p => p.ParameterType.Name.Contains("TenantOwner", StringComparison.OrdinalIgnoreCase));
    }

    private void SetupPosition(Guid positionId, Guid departmentId)
    {
        var position = new Position { Id = positionId, TenantId = _tenantId, DepartmentId = departmentId, IsActive = true, MaxOccupancy = 5 };
        _positionRepository
            .Setup(r => r.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
    }
}
