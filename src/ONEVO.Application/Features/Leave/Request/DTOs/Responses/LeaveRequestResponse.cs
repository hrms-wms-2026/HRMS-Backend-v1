namespace ONEVO.Application.Features.Leave.Request.DTOs.Responses;

public sealed record LeaveRequestResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    bool NoticePeriodMissed,
    Guid? SubmittedOnBehalfOfBy,
    LeaveRequestBalanceImpactResponse BalanceImpact,
    IReadOnlyList<LeaveRequestApproverResponse> Approvers,
    LeaveRequestConflictSnapshotResponse ConflictSnapshot,
    DateTimeOffset CreatedAt);

public sealed record LeaveRequestBalanceImpactResponse(
    decimal CurrentRemainingDays,
    decimal PendingAfterSubmitDays,
    decimal RemainingAfterSubmitDays);

public sealed record LeaveRequestApproverResponse(
    Guid ApproverEmployeeId,
    int SequenceOrder,
    string Status,
    Guid? DelegatedFromApproverId);

public sealed record LeaveRequestConflictSnapshotResponse(
    IReadOnlyList<LeaveRequestWarningResponse> Warnings,
    IReadOnlyList<LeaveRequestCalendarConflictResponse> CalendarConflicts,
    decimal? TeamAbsencePercent);

public sealed record LeaveRequestWarningResponse(
    string Code,
    string Message);

public sealed record LeaveRequestCalendarConflictResponse(
    string Source,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public sealed record LeaveRequestListItemResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    bool NoticePeriodMissed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
