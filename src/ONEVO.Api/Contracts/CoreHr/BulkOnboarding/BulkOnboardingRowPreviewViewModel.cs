namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record BulkOnboardingRowPreviewViewModel(
    string? FirstName, string? LastName, string? WorkEmail, string? StartDate,
    string? EmploymentType, string? WorkModeName, string? DepartmentName, string? PositionName,
    string? ChecklistTemplateName, string? EmployeeNumber);
