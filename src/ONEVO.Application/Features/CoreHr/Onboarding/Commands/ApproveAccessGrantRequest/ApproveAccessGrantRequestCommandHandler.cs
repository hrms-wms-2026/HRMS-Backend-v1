using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
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

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;

/// <summary>
/// Consumes a pending <see cref="AccessGrantRequest"/> created by
/// FinalizeOnboardingDraftCommandHandler's "defer everything" branch. Re-runs the same
/// validations that finalize itself runs (nothing staged there was ever persisted), then
/// performs the full user/employee/position-assignment/checklist/role/invitation creation in
/// one transaction and marks both the request and the draft decided.
///
/// Concurrency: this handler always mutates the onboarding draft (Status -> Finalized) and
/// saves through <see cref="IOnboardingDraftRepository.SaveChangesAsync"/>, which carries the
/// draft's own xmin optimistic-concurrency token. That is also what protects against an
/// approve/reject race on the same request: RejectAccessGrantRequestCommandHandler likewise
/// touches and saves the draft, so the two contend on the same row and the loser gets a clean
/// 409 instead of silently clobbering the other's decision.
/// </summary>
public class ApproveAccessGrantRequestCommandHandler
    : IRequestHandler<ApproveAccessGrantRequestCommand, Result<ApproveAccessGrantRequestResponse>>
{
    private const int InvitationValidityHours = 24;

    private readonly IAccessGrantRequestRepository _accessGrantRequestRepository;
    private readonly IOnboardingDraftRepository _draftRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly ILegalEntityRepository _legalEntityRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IEmploymentTypeRepository _employmentTypeRepository;
    private readonly IWorkModeRepository _workModeRepository;
    private readonly ISeatEntitlementService _seatEntitlementService;
    private readonly IChecklistTemplateRepository _checklistTemplateRepository;
    private readonly IEmployeeChecklistTaskRepository _checklistTaskRepository;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ApproveAccessGrantRequestCommandHandler(
        IAccessGrantRequestRepository accessGrantRequestRepository,
        IOnboardingDraftRepository draftRepository,
        IEmployeeRepository employeeRepository,
        IUserRepository userRepository,
        IUserRoleRepository userRoleRepository,
        IPositionRepository positionRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        ILegalEntityRepository legalEntityRepository,
        IDepartmentRepository departmentRepository,
        IEmploymentTypeRepository employmentTypeRepository,
        IWorkModeRepository workModeRepository,
        ISeatEntitlementService seatEntitlementService,
        IChecklistTemplateRepository checklistTemplateRepository,
        IEmployeeChecklistTaskRepository checklistTaskRepository,
        IInvitationTokenRepository invitationTokenRepository,
        ITenantRepository tenantRepository,
        IOutboxWriter outboxWriter,
        ISecureTokenGenerator tokenGenerator,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _accessGrantRequestRepository = accessGrantRequestRepository;
        _draftRepository = draftRepository;
        _employeeRepository = employeeRepository;
        _userRepository = userRepository;
        _userRoleRepository = userRoleRepository;
        _positionRepository = positionRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _legalEntityRepository = legalEntityRepository;
        _departmentRepository = departmentRepository;
        _employmentTypeRepository = employmentTypeRepository;
        _workModeRepository = workModeRepository;
        _seatEntitlementService = seatEntitlementService;
        _checklistTemplateRepository = checklistTemplateRepository;
        _checklistTaskRepository = checklistTaskRepository;
        _invitationTokenRepository = invitationTokenRepository;
        _tenantRepository = tenantRepository;
        _outboxWriter = outboxWriter;
        _tokenGenerator = tokenGenerator;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ApproveAccessGrantRequestResponse>> Handle(
        ApproveAccessGrantRequestCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var grantRequest = await _accessGrantRequestRepository.GetTrackedByIdAsync(tenantId, request.AccessGrantRequestId, ct);
        if (grantRequest is null)
            return Result<ApproveAccessGrantRequestResponse>.NotFound("The access grant request could not be found.");

        if (grantRequest.ApprovalStatus != "Pending")
            return Result<ApproveAccessGrantRequestResponse>.Conflict("This access grant request has already been decided.");

        if (grantRequest.OnboardingDraftId is null)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                "This access grant request is not correlated to an onboarding draft.");

        var draft = await _draftRepository.GetTrackedAsync(tenantId, grantRequest.OnboardingDraftId.Value, ct);
        if (draft is null)
            return Result<ApproveAccessGrantRequestResponse>.NotFound("The related onboarding draft could not be found.");

        if (draft.Status != OnboardingDraftStatus.WaitingForPositionApproval)
            return Result<ApproveAccessGrantRequestResponse>.Conflict(
                "This draft is not currently waiting for position access approval.");

        // ---- Revalidation. Nothing from finalize's deferred branch was ever persisted, so
        // every check finalize would have made must run again here before anything is created. ----
        if (string.IsNullOrWhiteSpace(draft.FirstName))
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("First name is required.");
        if (string.IsNullOrWhiteSpace(draft.LastName))
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("Last name is required.");
        if (string.IsNullOrWhiteSpace(draft.WorkEmail) || !IsValidEmail(draft.WorkEmail))
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("A valid work email is required.");

        var legalEntity = await _legalEntityRepository.GetByIdForTenantAsync(tenantId, draft.LegalEntityId, ct);
        if (legalEntity is null || !legalEntity.IsActive)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("The selected legal entity does not exist or is inactive.");

        if (draft.DepartmentId is not null)
        {
            var department = await _departmentRepository.GetByIdForLegalEntityAsync(tenantId, draft.LegalEntityId, draft.DepartmentId.Value, ct);
            if (department is null || !department.IsActive)
                return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                    "The selected department does not exist, is inactive, or does not belong to the selected legal entity.");
        }

        if (draft.PositionId is null)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("This draft has no selected position.");

        if (draft.PositionId.Value != grantRequest.TargetPositionId)
            return Result<ApproveAccessGrantRequestResponse>.Conflict(
                "The draft's selected position no longer matches this access grant request.");

        var position = await _positionRepository.GetByIdForLegalEntityAsync(tenantId, draft.LegalEntityId, draft.PositionId.Value, ct);
        if (position is null || !position.IsActive || (draft.DepartmentId is not null && position.DepartmentId != draft.DepartmentId))
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                "The selected position does not exist, is inactive, or does not match the selected legal entity and department.");

        if (position.DepartmentId is null)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("The selected position has no department and cannot be used.");

        if (!await _workModeRepository.ExistsActiveAsync(draft.WorkModeId, ct))
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("The selected work mode does not exist or is inactive.");

        var employmentTypeId = await _employmentTypeRepository.GetIdByCodeAsync(draft.EmploymentType, ct);
        if (employmentTypeId is null)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("The selected employment type does not exist.");

        if (await _employeeRepository.EmailExistsAsync(tenantId, draft.WorkEmail, excludeId: null, ct))
            return Result<ApproveAccessGrantRequestResponse>.Conflict("An employee with this work email already exists.");

        if (string.IsNullOrWhiteSpace(draft.EmployeeNumber))
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                "An employee number is required to approve this onboarding; no auto-generation policy exists yet.");

        if (await _employeeRepository.EmployeeNumberExistsAsync(tenantId, draft.EmployeeNumber, excludeId: null, ct))
            return Result<ApproveAccessGrantRequestResponse>.Conflict("This employee number already exists for this company.");

        var accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, draft.PositionId.Value, ct);
        if (accessTemplate is null || !accessTemplate.IsActive)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity("The position's access template is no longer active.");

        if (accessTemplate.Id != grantRequest.PositionAccessTemplateId)
            return Result<ApproveAccessGrantRequestResponse>.Conflict(
                "The position's access template has changed since this request was submitted.");

        if (accessTemplate.RoleId != grantRequest.RequestedRoleId)
            return Result<ApproveAccessGrantRequestResponse>.Conflict(
                "The position's access role has changed since this request was submitted.");

        // Capacity, same signal FinalizeOnboardingDraftCommandHandler uses.
        var activeAssignmentCount = await _positionAssignmentRepository.CountActiveAsync(tenantId, position.Id, ct);
        if (activeAssignmentCount >= position.MaxOccupancy)
            return Result<ApproveAccessGrantRequestResponse>.Conflict("This position has reached its capacity.");

        // ---- Seat recheck. Blocked/Undetermined create nothing and leave the request pending -
        // approving again later (once seats/billing are resolved) must still be possible. ----
        var seatDecision = await _seatEntitlementService.EvaluateAsync(tenantId, ct);
        if (seatDecision.Status == SeatDecisionStatus.Blocked)
            return Result<ApproveAccessGrantRequestResponse>.Conflict("No seat is available to approve this onboarding.");

        if (seatDecision.Status == SeatDecisionStatus.Undetermined)
            return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                "Seat availability cannot be determined for this tenant; billing configuration is required before this request can be approved.");

        // User is resolved (found-or-built, not yet persisted) before checklist instantiation:
        // InstantiateAsync needs a concrete new-hire user id to resolve any ownerType ==
        // "employee" checklist task (see ChecklistTaskJsonContract). Nothing is persisted until
        // the single SaveChangesAsync below, so this reorder changes nothing transactionally.
        var normalizedEmail = draft.WorkEmail.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByTenantAndEmailAsync(draft.TenantId, normalizedEmail, ct);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                TenantId = draft.TenantId,
                Email = draft.WorkEmail.Trim(),
                FirstName = draft.FirstName.Trim(),
                LastName = draft.LastName.Trim(),
                PasswordHash = string.Empty,
                IsActive = false,
                EmailVerified = false,
                MustChangePassword = true,
                PasswordSetByAdmin = false,
            };
            await _userRepository.AddAsync(user, ct);
        }

        // ---- Checklist. Staged only, nothing persisted until the single SaveChangesAsync below. ----
        ChecklistTemplate? template = null;
        if (draft.SelectedTemplateId is not null)
        {
            template = await _checklistTemplateRepository.GetActiveOnboardingAsync(
                draft.TenantId, draft.SelectedTemplateId.Value, draft.LegalEntityId, draft.DepartmentId, draft.PositionId, ct);
            if (template is null)
                return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                    "The selected onboarding checklist template does not exist, is inactive, or no longer applies to this company/department/position.");
        }

        var employeeId = Guid.NewGuid();
        var tasksCreated = 0;
        if (template is not null)
        {
            try
            {
                var tasks = await _checklistTaskRepository.InstantiateAsync(template, employeeId, user.Id, draft.EditedTasksJson, draft.StartDate, ct);
                tasksCreated = tasks.Count;
            }
            catch (ArgumentException)
            {
                return Result<ApproveAccessGrantRequestResponse>.UnprocessableEntity(
                    "Checklist task data is invalid: every task requires a title, a known owner type, a resolvable assignee, a due rule, and an explicit required flag.");
            }
        }

        var employee = new EmployeeEntity
        {
            Id = employeeId,
            TenantId = draft.TenantId,
            UserId = user.Id,
            EmployeeNumber = draft.EmployeeNumber!,
            FirstName = draft.FirstName.Trim(),
            LastName = draft.LastName.Trim(),
            Email = draft.WorkEmail.Trim(),
            DepartmentId = draft.DepartmentId,
            LegalEntityId = draft.LegalEntityId,
            EmploymentStatusId = 1,
            EmploymentTypeId = employmentTypeId.Value,
            WorkModeId = draft.WorkModeId,
            HireDate = draft.StartDate,
            CreatedById = _currentUser.UserId,
        };
        await _employeeRepository.AddAsync(employee, ct);

        var assignment = new PositionAssignmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = draft.TenantId,
            EmployeeId = employeeId,
            PositionId = position.Id,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            EffectiveFrom = draft.StartDate,
            AssignmentStatus = PositionAssignmentStatus.Active,
            CreatedById = _currentUser.UserId,
        };
        await _positionAssignmentRepository.AddAsync(assignment, ct);

        // UserRole's key is (UserId, RoleId), not scoped by position/tenant - if the matched
        // existing user already holds the requested role (e.g. from an earlier grant), adding it
        // again would be a primary-key violation, not a legitimate duplicate. Skip the insert in
        // that case rather than letting it surface as a misleading conflict.
        var existingRoles = await _userRoleRepository.ListActiveByUserIdAsync(user.Id, _clock.UtcNow, ct);
        if (!existingRoles.Any(ur => ur.RoleId == grantRequest.RequestedRoleId))
        {
            var userRole = new UserRole
            {
                TenantId = draft.TenantId,
                UserId = user.Id,
                RoleId = grantRequest.RequestedRoleId,
                AssignedAt = _clock.UtcNow,
                AssignedBy = _currentUser.UserId,
                SourcePositionId = draft.PositionId,
                SourcePositionAccessTemplateId = accessTemplate.Id,
            };
            await _userRoleRepository.AddAsync(userRole, ct);
        }

        var rawToken = _tokenGenerator.GenerateUrlSafeOpaqueToken();
        var tokenHash = InvitationTokenHasher.Hash(rawToken);
        var expiresAt = _clock.UtcNow.AddHours(InvitationValidityHours);
        var fullName = $"{draft.FirstName.Trim()} {draft.LastName.Trim()}".Trim();

        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = draft.TenantId,
            UserId = user.Id,
            RoleId = null,
            PositionId = draft.PositionId,
            Purpose = InvitationToken.EmployeeOnboardingPurpose,
            LegalEntityId = draft.LegalEntityId,
            EmployeeId = employeeId,
            OnboardingDraftId = draft.Id,
            InvitedEmail = draft.WorkEmail.Trim(),
            InvitedFullName = fullName,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = _clock.UtcNow,
            CreatedById = _currentUser.UserId,
        };
        await _invitationTokenRepository.AddAsync(invitation, ct);

        var tenant = await _tenantRepository.GetByIdAsync(draft.TenantId, ct);
        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.EmployeeOnboardingInviteEmail,
            new EmployeeOnboardingInviteEmailPayload(
                draft.TenantId, draft.LegalEntityId, employeeId, invitation.Id,
                draft.WorkEmail.Trim(), draft.FirstName.Trim(), draft.LastName.Trim(), rawToken, expiresAt,
                tenant?.Slug),
            draft.TenantId,
            ct);

        grantRequest.ApprovalStatus = "Approved";
        grantRequest.DecidedByUserId = _currentUser.UserId;
        grantRequest.DecidedAt = _clock.UtcNow;
        grantRequest.EmployeeId = employeeId;
        grantRequest.UserId = user.Id;

        draft.Status = OnboardingDraftStatus.Finalized;
        draft.DraftReason = OnboardingDraftReason.InvitationSent;
        draft.FinalizedAt = _clock.UtcNow;
        draft.UpdatedAt = _clock.UtcNow;

        var saveResult = await SaveAsync(ct);
        if (saveResult is not null)
            return saveResult;

        return Result<ApproveAccessGrantRequestResponse>.Success(new ApproveAccessGrantRequestResponse(
            grantRequest.Id, draft.Id, employeeId, draft.Status,
            InvitationQueued: true, ChecklistTaskCount: tasksCreated, PositionApprovalStatus: grantRequest.ApprovalStatus,
            MessageKey: "onboarding.access_grant.approved"));
    }

    /// <summary>Saves everything staged on the shared DbContext through the draft repository so
    /// the draft's xmin token participates in the concurrency check (see class doc comment).
    /// Returns null on success, or a Result to return immediately on conflict.</summary>
    private async Task<Result<ApproveAccessGrantRequestResponse>?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await _draftRepository.SaveChangesAsync(ct);
            return null;
        }
        catch (ConcurrencyConflictException)
        {
            return Result<ApproveAccessGrantRequestResponse>.Conflict(
                "This request was just updated by someone else. Please refresh and try again.");
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<ApproveAccessGrantRequestResponse>.Conflict(
                "This request conflicts with an existing record (e.g. a duplicate email or employee number). Please refresh and try again.");
        }
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
