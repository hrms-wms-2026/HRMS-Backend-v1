using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>
/// Converts a valid onboarding draft into a pending employee onboarding package.
///
/// The RequiresApproval branch follows the employee-onboarding userflow doc's "Sensitive
/// Position Approval" section literally: while approval is pending, the employee remains in a
/// Draft-family state and no user, employee, position assignment, checklist task, invitation
/// token, or outbox record is created - only the access grant request is submitted. A separate,
/// not-yet-built "Approve & Send Invite" flow performs the actual creation once approved. This
/// diverges from a plain reading of this endpoint's own task instructions (which implied the
/// employee/user lifecycle is created eagerly and only the role is deferred); the userflow doc
/// was treated as authoritative per explicit product direction.
/// </summary>
public class OnboardingDraftWriteService : IOnboardingDraftWriteService
{
    private const int InvitationValidityHours = 24;

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
    private readonly IAccessGrantRequestRepository _accessGrantRequestRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IChecklistTemplateRepository _checklistTemplateRepository;
    private readonly IEmployeeChecklistTaskRepository _checklistTaskRepository;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public OnboardingDraftWriteService(
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
        IAccessGrantRequestRepository accessGrantRequestRepository,
        IPermissionRepository permissionRepository,
        IChecklistTemplateRepository checklistTemplateRepository,
        IEmployeeChecklistTaskRepository checklistTaskRepository,
        IInvitationTokenRepository invitationTokenRepository,
        ITenantRepository tenantRepository,
        IOutboxWriter outboxWriter,
        ISecureTokenGenerator tokenGenerator,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
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
        _accessGrantRequestRepository = accessGrantRequestRepository;
        _permissionRepository = permissionRepository;
        _checklistTemplateRepository = checklistTemplateRepository;
        _checklistTaskRepository = checklistTaskRepository;
        _invitationTokenRepository = invitationTokenRepository;
        _tenantRepository = tenantRepository;
        _outboxWriter = outboxWriter;
        _tokenGenerator = tokenGenerator;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<OnboardingDraftResponse>> SaveAsync(
        Guid tenantId, Guid actingUserId, SaveOnboardingDraftCommand request, CancellationToken ct)
    {
        if (!await _workModeRepository.ExistsActiveAsync(request.WorkModeId, ct))
        {
            return Result<OnboardingDraftResponse>.Failure("The selected work mode does not exist or is inactive.");
        }

        var legalEntity = await _legalEntityRepository.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null || !legalEntity.IsActive)
            return Result<OnboardingDraftResponse>.Failure("The selected legal entity does not exist or is inactive.");

        if (request.DepartmentId is not null)
        {
            var department = await _departmentRepository.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId.Value, ct);
            if (department is null || !department.IsActive)
                return Result<OnboardingDraftResponse>.Failure("The selected department does not exist, is inactive, or does not belong to the selected legal entity.");
        }

        if (request.PositionId is not null)
        {
            var position = await _positionRepository.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId.Value, ct);
            if (position is null || !position.IsActive || (request.DepartmentId is not null && position.DepartmentId != request.DepartmentId))
                return Result<OnboardingDraftResponse>.Failure("The selected position does not exist, is inactive, or does not match the selected legal entity and department.");
        }

        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, request.LegalEntityId, request.WorkEmail, excludeId: null, ct))
        {
            return Result<OnboardingDraftResponse>.Conflict("An employee with this work email already exists in this company.");
        }

        if (request.EmployeeNumber is not null
            && await _employeeRepository.EmployeeNumberExistsAsync(tenantId, request.EmployeeNumber, excludeId: null, ct))
        {
            return Result<OnboardingDraftResponse>.Conflict("This employee number is already in use.");
        }

        OnboardingDraftEntity draft;
        if (request.DraftId is null)
        {
            draft = new OnboardingDraftEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                StartedById = actingUserId,
                CreatedById = actingUserId,
            };
        }
        else
        {
            var existing = await _draftRepository.GetTrackedAsync(tenantId, request.DraftId.Value, ct);
            if (existing is null)
            {
                return Result<OnboardingDraftResponse>.NotFound("The draft could not be found.");
            }

            if (existing.StartedById != actingUserId && !_currentUser.HasPermission("employees:write"))
            {
                return Result<OnboardingDraftResponse>.Forbidden();
            }

            if (request.IfMatchVersion is not null)
            {
                _draftRepository.SetExpectedVersion(existing, request.IfMatchVersion);
            }

            draft = existing;
        }

        // Reason resolution order: position approval requirement first (a real, computable
        // signal from PositionAccessTemplate.RequiresApproval), then seat availability. Every
        // path in this slice ends in a Draft status - there is no path to Finalized because
        // final employee creation is not implemented (no generic invitation/account-creation
        // dependency exists yet; see EMPLOYEE_MANAGEMENT_IMPLEMENTATION_REPORT.md).
        string status;
        string? reason;

        var requiresApproval = false;
        if (request.PositionId is not null)
        {
            var accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(
                tenantId, request.PositionId.Value, ct);
            requiresApproval = accessTemplate is { IsActive: true, RequiresApproval: true };
        }

        if (requiresApproval)
        {
            status = OnboardingDraftStatus.WaitingForPositionApproval;
            reason = OnboardingDraftReason.WaitingForPositionApproval;
        }
        else
        {
            var seatDecision = await _seatEntitlementService.EvaluateAsync(tenantId, ct);
            if (seatDecision.Status == SeatDecisionStatus.Undetermined)
            {
                status = OnboardingDraftStatus.Draft;
                reason = OnboardingDraftReason.SeatConfigurationRequired;
            }

            else if (seatDecision.Status == SeatDecisionStatus.Blocked)
            {
                status = OnboardingDraftStatus.WaitingForSeat;
                reason = OnboardingDraftReason.WaitingForSeat;
            }
            else
            {
                status = OnboardingDraftStatus.Draft;
                reason = OnboardingDraftReason.SavedManually;
            }
        }

        draft.FirstName = request.FirstName.Trim();
        draft.LastName = request.LastName.Trim();
        draft.WorkEmail = request.WorkEmail.Trim();
        draft.LegalEntityId = request.LegalEntityId;
        draft.DepartmentId = request.DepartmentId;
        draft.PositionId = request.PositionId;
        draft.EmploymentType = request.EmploymentType;
        draft.StartDate = request.StartDate;
        draft.EmployeeNumber = request.EmployeeNumber;
        draft.WorkModeId = request.WorkModeId;
        draft.SelectedTemplateId = request.SelectedTemplateId;
        draft.EditedTasksJson = request.EditedTasksJson;
        draft.LastSavedStep = request.LastSavedStep;
        draft.Status = status;
        draft.DraftReason = reason;
        draft.UpdatedAt = _clock.UtcNow;

        if (request.DraftId is null)
        {
            await _draftRepository.AddAsync(draft, ct);
        }

        try
        {
            await _draftRepository.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<OnboardingDraftResponse>.Conflict(
                "This draft was just updated by someone else. Please refresh and try again.");
        }

        var response = await _draftRepository.GetResponseByIdAsync(tenantId, draft.Id, ct);
        return Result<OnboardingDraftResponse>.Success(response!);
    }

    public async Task<Result<FinalizeOnboardingDraftResponse>> FinalizeAsync(
        Guid tenantId, Guid actingUserId, Guid draftId, CancellationToken ct)
    {
        var draft = await _draftRepository.GetTrackedAsync(tenantId, draftId, ct);
        if (draft is null)
            return Result<FinalizeOnboardingDraftResponse>.NotFound("The draft could not be found.");

        if (draft.Status == OnboardingDraftStatus.Cancelled)
            return Result<FinalizeOnboardingDraftResponse>.Conflict("This draft has been cancelled and cannot be finalized.");

        if (draft.Status == OnboardingDraftStatus.Finalized)
            return Result<FinalizeOnboardingDraftResponse>.Conflict("This draft has already been finalized.");

        if (draft.Status == OnboardingDraftStatus.WaitingForPositionApproval)
        {
            // Only block when a decision is genuinely still outstanding. A prior pending request
            // that was since rejected does not keep the draft permanently stuck: RejectAccessGrantRequestCommandHandler
            // moves the draft back to Draft/PositionApprovalRejected, but if HR re-saves the draft
            // via PUT without changing the (still approval-requiring) position,
            // SaveOnboardingDraftCommandHandler recomputes and re-stamps WaitingForPositionApproval
            // even though nothing is actually pending. Checking for a live Pending row (rather than
            // trusting this status flag alone) is what lets finalize re-evaluate and submit a fresh
            // request in that case instead of 409ing forever.
            var hasOutstandingRequest = await _accessGrantRequestRepository.AnyPendingByDraftAsync(tenantId, draft.Id, ct);
            if (hasOutstandingRequest)
                return Result<FinalizeOnboardingDraftResponse>.Conflict(
                    "This draft is waiting for position access approval and cannot be finalized again until that is resolved.");
        }

        // ---- Field validation. SaveOnboardingDraftCommandValidator already enforces most of
        // this at save time, but finalize must not trust persisted state blindly either. ----
        if (string.IsNullOrWhiteSpace(draft.FirstName))
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("First name is required.");
        if (string.IsNullOrWhiteSpace(draft.LastName))
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("Last name is required.");
        if (string.IsNullOrWhiteSpace(draft.WorkEmail) || !IsValidEmail(draft.WorkEmail))
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("A valid work email is required.");

        var legalEntity = await _legalEntityRepository.GetByIdForTenantAsync(tenantId, draft.LegalEntityId, ct);
        if (legalEntity is null || !legalEntity.IsActive)
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("The selected legal entity does not exist or is inactive.");

        if (draft.DepartmentId is not null)
        {
            var department = await _departmentRepository.GetByIdForLegalEntityAsync(tenantId, draft.LegalEntityId, draft.DepartmentId.Value, ct);
            if (department is null || !department.IsActive)
                return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity(
                    "The selected department does not exist, is inactive, or does not belong to the selected legal entity.");
        }

        Position? position = null;
        if (draft.PositionId is not null)
        {
            position = await _positionRepository.GetByIdForLegalEntityAsync(tenantId, draft.LegalEntityId, draft.PositionId.Value, ct);
            if (position is null || !position.IsActive || (draft.DepartmentId is not null && position.DepartmentId != draft.DepartmentId))
                return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity(
                    "The selected position does not exist, is inactive, or does not match the selected legal entity and department.");

            // TargetDepartmentId on the access grant request (and on the position assignment's
            // department consistency) needs a real department; a position with no department is
            // legacy/anomalous data this handler does not attempt to repair.
            if (position.DepartmentId is null)
                return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("The selected position has no department and cannot be used.");
        }

        if (!await _workModeRepository.ExistsActiveAsync(draft.WorkModeId, ct))
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("The selected work mode does not exist or is inactive.");

        var employmentTypeId = await _employmentTypeRepository.GetIdByCodeAsync(draft.EmploymentType, ct);
        if (employmentTypeId is null)
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity("The selected employment type does not exist.");

        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, draft.LegalEntityId, draft.WorkEmail, excludeId: null, ct))
            return Result<FinalizeOnboardingDraftResponse>.Conflict("An employee with this work email already exists in this company.");

        // EmployeeNumber is "conditional" per product docs (required when not auto-generated),
        // but no auto-generation policy exists in this codebase, and Employee.EmployeeNumber is
        // a non-nullable, tenant-unique column - so it is treated as required here.
        if (string.IsNullOrWhiteSpace(draft.EmployeeNumber))
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity(
                "An employee number is required to finalize onboarding; no auto-generation policy exists yet.");

        if (await _employeeRepository.EmployeeNumberExistsAsync(tenantId, draft.EmployeeNumber, excludeId: null, ct))
            return Result<FinalizeOnboardingDraftResponse>.Conflict("This employee number already exists for this company.");

        PositionAccessTemplate? accessTemplate = null;
        if (draft.PositionId is not null)
        {
            accessTemplate = await _positionRepository.GetAccessTemplateByPositionAsync(tenantId, draft.PositionId.Value, ct);
        }
        var requiresApproval = accessTemplate is { IsActive: true, RequiresApproval: true };

        if (requiresApproval)
        {
            return await FinalizeWithPendingApprovalAsync(draft, accessTemplate!, position!, actingUserId, ct);
        }

        return await FinalizeImmediatelyAsync(draft, accessTemplate, position, employmentTypeId.Value, actingUserId, ct);
    }

    private async Task<Result<FinalizeOnboardingDraftResponse>> FinalizeWithPendingApprovalAsync(
        OnboardingDraftEntity draft, PositionAccessTemplate accessTemplate, Position position, Guid actingUserId, CancellationToken ct)
    {
        var existingPending = await _accessGrantRequestRepository.GetPendingByDraftAsync(
            draft.TenantId, draft.Id, draft.PositionId!.Value, accessTemplate.Id, ct);

        if (existingPending is null)
        {
            var grantRequest = new AccessGrantRequest
            {
                Id = Guid.NewGuid(),
                TenantId = draft.TenantId,
                EmployeeId = null,
                UserId = null,
                OnboardingDraftId = draft.Id,
                ActionType = AccessGrantActionType.EmployeeOnboarding,
                TargetPositionId = draft.PositionId.Value,
                TargetDepartmentId = position.DepartmentId!.Value,
                PositionAccessTemplateId = accessTemplate.Id,
                RequestedRoleId = accessTemplate.RoleId,
                ApprovalStatus = "Pending",
                RequestedByUserId = actingUserId,
                RequestedAt = _clock.UtcNow,
                EffectiveFrom = ToUtcMidnight(draft.StartDate),
                EffectiveTo = null,
            };
            await _accessGrantRequestRepository.AddAsync(grantRequest, ct);

            var approverUserIds = await _permissionRepository.ListUserIdsWithPermissionCodeAsync(
                draft.TenantId, "roles:manage", _clock.UtcNow, ct);
            var tenantSlug = (await _tenantRepository.GetByIdAsync(draft.TenantId, ct))?.Slug;
            foreach (var approverUserId in approverUserIds)
            {
                var approver = await _userRepository.GetByIdAsync(approverUserId, ct);
                if (approver is null) continue;

                await _outboxWriter.EnqueueAsync(
                    OutboxMessageTypes.PositionChangeApprovalRequestEmail,
                    new PositionChangeApprovalRequestEmailPayload(
                        draft.TenantId, approverUserId, grantRequest.Id, approver.Email,
                        $"{draft.FirstName} {draft.LastName}".Trim(), position.Name, grantRequest.ChangeReason,
                        tenantSlug),
                    draft.TenantId, ct);
            }
        }

        draft.Status = OnboardingDraftStatus.WaitingForPositionApproval;
        draft.DraftReason = OnboardingDraftReason.WaitingForPositionApproval;
        draft.UpdatedAt = _clock.UtcNow;

        var saveResult = await PersistChangesAsync(ct);
        if (saveResult is not null)
            return saveResult;

        return Result<FinalizeOnboardingDraftResponse>.Success(new FinalizeOnboardingDraftResponse(
            draft.Id, null, draft.Status, draft.DraftReason,
            InvitationQueued: false, PositionApprovalPending: true, ChecklistTasksCreated: false,
            MessageKey: "onboarding.finalize.waiting_for_position_approval"));
    }

    private async Task<Result<FinalizeOnboardingDraftResponse>> FinalizeImmediatelyAsync(
        OnboardingDraftEntity draft, PositionAccessTemplate? accessTemplate, Position? position, int employmentTypeId, Guid actingUserId, CancellationToken ct)
    {
        var seatDecision = await _seatEntitlementService.EvaluateAsync(draft.TenantId, ct);
        if (seatDecision.Status == SeatDecisionStatus.Blocked)
        {
            draft.Status = OnboardingDraftStatus.WaitingForSeat;
            draft.DraftReason = OnboardingDraftReason.WaitingForSeat;
            draft.UpdatedAt = _clock.UtcNow;

            var saveResult = await PersistChangesAsync(ct);
            if (saveResult is not null)
                return saveResult;

            return Result<FinalizeOnboardingDraftResponse>.Conflict("No seat is available to finalize this onboarding.");
        }

        if (seatDecision.Status == SeatDecisionStatus.Undetermined)
        {
            return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity(
                "Seat availability cannot be determined for this tenant; billing configuration is required before onboarding can be finalized.");
        }

        // User is resolved (found-or-built, not yet persisted) before checklist instantiation:
        // InstantiateAsync needs a concrete new-hire user id to resolve any ownerType ==
        // "employee" checklist task (see ChecklistTaskJsonContract). Nothing is persisted until
        // the single SaveChangesAsync at the end, so this reorder changes nothing transactionally.
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

        // Checklist template + task JSON are validated (and, via InstantiateAsync, staged)
        // before anything else is created below - nothing is persisted until the single
        // SaveChangesAsync at the end, so a JSON error here still fails clean.
        ChecklistTemplate? template = null;
        if (draft.SelectedTemplateId is not null)
        {
            template = await _checklistTemplateRepository.GetActiveOnboardingAsync(
                draft.TenantId, draft.SelectedTemplateId.Value, draft.LegalEntityId, draft.DepartmentId, draft.PositionId, ct);
            if (template is null)
                return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity(
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
                return Result<FinalizeOnboardingDraftResponse>.UnprocessableEntity(
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
            // No "onboarding" row exists in the tenant-wide employment_statuses lookup (it is
            // read app-wide, e.g. the employee list falls back to "active"), so pending-ness is
            // carried by User.IsActive = false, InvitationToken.Status = "pending", and this
            // draft's own Status/FinalizedAt instead of an employee-level status value.
            EmploymentStatusId = 1,
            EmploymentTypeId = employmentTypeId,
            WorkModeId = draft.WorkModeId,
            HireDate = draft.StartDate,
            CreatedById = actingUserId,
        };
        await _employeeRepository.AddAsync(employee, ct);

        Guid? reservedAssignmentId = null;
        if (position is not null)
        {
            reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
                draft.TenantId, employeeId, position.Id, draft.StartDate, actingUserId, ct);
            if (reservedAssignmentId is null)
                return Result<FinalizeOnboardingDraftResponse>.Conflict("This position has reached its capacity.");
        }

        // The only role ever assigned here is the position access template's own RoleId - never
        // a hardcoded Owner/Admin default.
        if (accessTemplate is { IsActive: true, RequiresApproval: false })
        {
            var userRole = new UserRole
            {
                TenantId = draft.TenantId,
                UserId = user.Id,
                RoleId = accessTemplate.RoleId,
                AssignedAt = _clock.UtcNow,
                AssignedBy = actingUserId,
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
            PositionAssignmentId = reservedAssignmentId,
            Purpose = InvitationToken.EmployeeOnboardingPurpose,
            LegalEntityId = draft.LegalEntityId,
            EmployeeId = employeeId,
            OnboardingDraftId = draft.Id,
            InvitedEmail = draft.WorkEmail.Trim(),
            InvitedFullName = fullName,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = _clock.UtcNow,
            CreatedById = actingUserId,
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

        draft.Status = OnboardingDraftStatus.Finalized;
        draft.DraftReason = OnboardingDraftReason.InvitationSent;
        draft.FinalizedAt = _clock.UtcNow;
        draft.UpdatedAt = _clock.UtcNow;

        var finalSaveResult = await PersistChangesAsync(ct);
        if (finalSaveResult is not null)
            return finalSaveResult;

        return Result<FinalizeOnboardingDraftResponse>.Success(new FinalizeOnboardingDraftResponse(
            draft.Id, employeeId, draft.Status, draft.DraftReason,
            InvitationQueued: true, PositionApprovalPending: false, ChecklistTasksCreated: tasksCreated > 0,
            MessageKey: "onboarding.finalize.invitation_sent"));
    }

    /// <summary>Saves all changes staged on the shared DbContext in one transaction. Returns
    /// null on success, or a Result to return immediately on a concurrency/uniqueness
    /// conflict.</summary>
    private async Task<Result<FinalizeOnboardingDraftResponse>?> PersistChangesAsync(CancellationToken ct)
    {
        try
        {
            await _draftRepository.SaveChangesAsync(ct);
            return null;
        }
        catch (ConcurrencyConflictException)
        {
            return Result<FinalizeOnboardingDraftResponse>.Conflict(
                "This draft was just updated by someone else. Please refresh and try again.");
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<FinalizeOnboardingDraftResponse>.Conflict(
                "This request conflicts with an existing record (e.g. a duplicate email, employee number, or a request already submitted). Please refresh and try again.");
        }
    }

    private static DateTimeOffset ToUtcMidnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

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
