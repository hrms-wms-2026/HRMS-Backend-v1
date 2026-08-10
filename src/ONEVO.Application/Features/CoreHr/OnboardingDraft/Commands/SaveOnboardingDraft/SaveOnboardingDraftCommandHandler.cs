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
    private readonly ISeatEntitlementService _seatEntitlementService;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SaveOnboardingDraftCommandHandler(
        IOnboardingDraftRepository draftRepository,
        IEmployeeRepository employeeRepository,
        IPositionRepository positionRepository,
        ISeatEntitlementService seatEntitlementService,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _draftRepository = draftRepository;
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _seatEntitlementService = seatEntitlementService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<OnboardingDraftResponse>> Handle(SaveOnboardingDraftCommand request, CancellationToken ct)
    {
        if (await _employeeRepository.EmailExistsAsync(_currentUser.TenantId, request.WorkEmail, excludeId: null, ct))
        {
            return Result<OnboardingDraftResponse>.Conflict("This work email is already in use.");
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
            if (seatDecision.Status != SeatDecisionStatus.Approved)
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

        draft.EmployeeName = request.EmployeeName;
        draft.WorkEmail = request.WorkEmail;
        draft.LegalEntityId = request.LegalEntityId;
        draft.DepartmentId = request.DepartmentId;
        draft.PositionId = request.PositionId;
        draft.EmploymentType = request.EmploymentType;
        draft.StartDate = request.StartDate;
        draft.EmployeeNumber = request.EmployeeNumber;
        draft.ScheduleId = request.ScheduleId;
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
