using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public record SaveOnboardingDraftCommand(
    Guid? DraftId,
    string EmployeeName,
    string WorkEmail,
    Guid LegalEntityId,
    Guid? DepartmentId,
    Guid? PositionId,
    string EmploymentType,
    DateOnly StartDate,
    string? EmployeeNumber,
    Guid? ScheduleId,
    Guid? SelectedTemplateId,
    string? EditedTasksJson,
    string LastSavedStep,
    string? IfMatchVersion) : IRequest<Result<OnboardingDraftResponse>>;
