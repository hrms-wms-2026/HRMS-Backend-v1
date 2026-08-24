using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public record SaveOnboardingDraftCommand(
    Guid? DraftId,
    string FirstName,
    string LastName,
    string WorkEmail,
    Guid LegalEntityId,
    Guid? DepartmentId,
    Guid? PositionId,
    string EmploymentType,
    DateOnly StartDate,
    string? EmployeeNumber,
    int WorkModeId,
    Guid? SelectedTemplateId,
    string? EditedTasksJson,
    string LastSavedStep,
    string? IfMatchVersion,
    Guid? ReportsToEmployeeId) : IRequest<Result<OnboardingDraftResponse>>;
