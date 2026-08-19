namespace ONEVO.Api.Contracts.CoreHr.OnboardingDrafts;

public record SaveOnboardingDraftRequest(
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
    Guid? ReportsToEmployeeId = null);
