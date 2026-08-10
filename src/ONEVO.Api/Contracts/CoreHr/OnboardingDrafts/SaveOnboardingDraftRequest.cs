namespace ONEVO.Api.Contracts.CoreHr.OnboardingDrafts;

public record SaveOnboardingDraftRequest(
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
    string LastSavedStep);
