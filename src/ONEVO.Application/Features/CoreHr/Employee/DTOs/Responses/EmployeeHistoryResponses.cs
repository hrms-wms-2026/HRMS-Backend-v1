namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Kind: joined | probation_completed | position_changed | manager_changed | leave_approved |
/// checklist_completed | terminated. Date is the UTC date of the event.</summary>
public sealed record EmployeeHistoryEvent(string Kind, string Title, string? Detail, DateOnly Date);

public sealed record EmployeeHistoryResponse(IReadOnlyList<EmployeeHistoryEvent> Events);
