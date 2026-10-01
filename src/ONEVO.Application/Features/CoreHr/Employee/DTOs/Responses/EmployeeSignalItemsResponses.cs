namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>One concrete thing behind a Needs Attention signal. Kind is attendance_day |
/// monitoring_alert | exception_case | task | approval; Id is the record id, or the yyyy-MM-dd date
/// for attendance_day. Task-only and approval-only fields are null for other kinds.</summary>
public sealed record EmployeeSignalItem(
    string Kind,
    string Id,
    DateOnly Date,
    string Title,
    string? Subtitle,
    DateTimeOffset? OccurredAt = null,
    Guid? ProjectId = null,
    string? ProjectName = null,
    DateOnly? DueDate = null,
    string? ApprovalKind = null,
    DateTimeOffset? RequestedAt = null,
    string? ApproverName = null);

/// <summary>Total is the full count (equals the signal's Value); Items holds at most 50, newest
/// first. Timezone is the employee's attendance timezone for attendance and alert keys.</summary>
public sealed record EmployeeSignalItemsResponse(
    string Key, int Total, string? Timezone, IReadOnlyList<EmployeeSignalItem> Items);
