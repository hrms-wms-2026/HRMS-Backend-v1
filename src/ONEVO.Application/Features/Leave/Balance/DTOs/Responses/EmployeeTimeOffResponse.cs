namespace ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

public sealed record EmployeeTimeOffBalance(
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal EntitledHours,
    decimal UsedHours,
    decimal PendingHours,
    decimal RemainingHours,
    bool IsNegative);

public sealed record EmployeeUpcomingLeave(
    Guid LeaveTypeId,
    string? LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalHours);

/// <summary>Balances are in hours (the leave module has no hours-per-day constant). NextLeave is
/// the earliest approved leave that has not ended, within the next 90 days.</summary>
public sealed record EmployeeTimeOffResponse(
    int Year,
    IReadOnlyList<EmployeeTimeOffBalance> Balances,
    EmployeeUpcomingLeave? NextLeave);
