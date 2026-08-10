namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

public record OnboardingDraftResponse(
    Guid Id,
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
    string Status,
    string? DraftReason,
    string LastSavedStep,
    Guid StartedById,
    string Version);
