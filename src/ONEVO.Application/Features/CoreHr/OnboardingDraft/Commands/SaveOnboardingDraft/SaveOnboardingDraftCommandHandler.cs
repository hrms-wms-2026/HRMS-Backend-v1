using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public class SaveOnboardingDraftCommandHandler : IRequestHandler<SaveOnboardingDraftCommand, Result<OnboardingDraftResponse>>
{
    private readonly IOnboardingDraftRepository _draftRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly ILegalEntityRepository _legalEntityRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ISeatEntitlementService _seatEntitlementService;
    private readonly IWorkModeRepository _workModeRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SaveOnboardingDraftCommandHandler(
        IOnboardingDraftRepository draftRepository,
        IEmployeeRepository employeeRepository,
        IPositionRepository positionRepository,
        ILegalEntityRepository legalEntityRepository,
        IDepartmentRepository departmentRepository,
        ISeatEntitlementService seatEntitlementService,
        IWorkModeRepository workModeRepository,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _draftRepository = draftRepository;
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _legalEntityRepository = legalEntityRepository;
        _departmentRepository = departmentRepository;
        _seatEntitlementService = seatEntitlementService;
        _workModeRepository = workModeRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<OnboardingDraftResponse>> Handle(SaveOnboardingDraftCommand request, CancellationToken ct)
    {
        if (!await _workModeRepository.ExistsActiveAsync(request.WorkModeId, ct))
        {
            return Result<OnboardingDraftResponse>.Failure("The selected work mode does not exist or is inactive.");
        }

        var legalEntity = await _legalEntityRepository.GetByIdForTenantAsync(_currentUser.TenantId, request.LegalEntityId, ct);
        if (legalEntity is null || !legalEntity.IsActive)
            return Result<OnboardingDraftResponse>.Failure("The selected legal entity does not exist or is inactive.");

        if (request.DepartmentId is not null)
        {
            var department = await _departmentRepository.GetByIdForLegalEntityAsync(_currentUser.TenantId, request.LegalEntityId, request.DepartmentId.Value, ct);
            if (department is null || !department.IsActive)
                return Result<OnboardingDraftResponse>.Failure("The selected department does not exist, is inactive, or does not belong to the selected legal entity.");
        }

        if (request.PositionId is not null)
        {
            var position = await _positionRepository.GetByIdForLegalEntityAsync(_currentUser.TenantId, request.LegalEntityId, request.PositionId.Value, ct);
            if (position is null || !position.IsActive || (request.DepartmentId is not null && position.DepartmentId != request.DepartmentId))
                return Result<OnboardingDraftResponse>.Failure("The selected position does not exist, is inactive, or does not match the selected legal entity and department.");
        }

        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(_currentUser.TenantId, request.LegalEntityId, request.WorkEmail, excludeId: null, ct))
        {
            return Result<OnboardingDraftResponse>.Conflict("An employee with this work email already exists in this company.");
        }

        if (request.EmployeeNumber is not null
            && await _employeeRepository.EmployeeNumberExistsAsync(_currentUser.TenantId, request.EmployeeNumber, excludeId: null, ct))
        {
            return Result<OnboardingDraftResponse>.Conflict("This employee number is already in use.");
        }

        OnboardingDraftEntity draft;
        if (request.DraftId is null)
        {
            draft = new OnboardingDraftEntity
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId,
                StartedById = _currentUser.UserId,
                CreatedById = _currentUser.UserId,
            };
        }
        else
        {
            var existing = await _draftRepository.GetTrackedAsync(_currentUser.TenantId, request.DraftId.Value, ct);
            if (existing is null)
            {
                return Result<OnboardingDraftResponse>.NotFound("The draft could not be found.");
            }

            if (existing.StartedById != _currentUser.UserId && !_currentUser.HasPermission("employees:write"))
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
                _currentUser.TenantId, request.PositionId.Value, ct);
            requiresApproval = accessTemplate is { IsActive: true, RequiresApproval: true };
        }

        if (requiresApproval)
        {
            status = OnboardingDraftStatus.WaitingForPositionApproval;
            reason = OnboardingDraftReason.WaitingForPositionApproval;
        }
        else
        {
            var seatDecision = await _seatEntitlementService.EvaluateAsync(_currentUser.TenantId, ct);
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

        var response = await _draftRepository.GetResponseByIdAsync(_currentUser.TenantId, draft.Id, ct);
        return Result<OnboardingDraftResponse>.Success(response!);
    }
}
