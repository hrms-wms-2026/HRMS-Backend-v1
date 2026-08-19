namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

public sealed record OffboardingOverviewItemResponse(
    Guid EmployeeId, string EmployeeName, string? DepartmentName, string? PositionName,
    string? CurrentOffboardingStatus, bool CanStartOffboarding);
