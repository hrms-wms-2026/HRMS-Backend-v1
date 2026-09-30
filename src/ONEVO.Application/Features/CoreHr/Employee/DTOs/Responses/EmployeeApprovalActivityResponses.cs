namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Status is one of pending | approved | rejected | cancelled. ApproverName is only known
/// for Work Management requests.</summary>
public sealed record EmployeeApprovalItem(
    string Id,
    string Kind,
    string Label,
    string? Detail,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? ApproverName);

public sealed record EmployeeApprovalActivityResponse(
    DateOnly From,
    DateOnly To,
    int Pending,
    int Approved,
    int Rejected,
    int Total,
    IReadOnlyList<EmployeeApprovalItem> Items);
