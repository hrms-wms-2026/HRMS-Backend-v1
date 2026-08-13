namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

public record OnboardingDraftResponse(
    Guid Id,
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
    string Status,
    string? DraftReason,
    string LastSavedStep,
    Guid StartedById,
    string Version);
