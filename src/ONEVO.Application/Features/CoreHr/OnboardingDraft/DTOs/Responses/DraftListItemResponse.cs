namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

public record DraftListItemResponse(
    Guid Id,
    string EmployeeName,
    string? PositionName,
    string? DepartmentName,
    string Status,
    string? DraftReason,
    string LastSavedStep,
    string StartedByName);

public record DraftListPageResponse(
    IReadOnlyList<DraftListItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
