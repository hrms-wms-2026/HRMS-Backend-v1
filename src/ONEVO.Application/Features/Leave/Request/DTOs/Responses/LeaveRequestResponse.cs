namespace ONEVO.Application.Features.Leave.Request.DTOs.Responses;

public sealed record LeaveRequestResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal TotalHours,
    decimal PaidHours,
    decimal UnpaidHours,
    string Status,
    bool NoticePeriodMissed,
    Guid? SubmittedOnBehalfOfBy,
    LeaveRequestBalanceImpactResponse BalanceImpact,
    IReadOnlyList<LeaveRequestApproverResponse> Approvers,
    LeaveRequestConflictSnapshotResponse ConflictSnapshot,
    DateTimeOffset CreatedAt);

public sealed record LeaveRequestBalanceImpactResponse(
    decimal CurrentRemainingHours,
    decimal PendingAfterSubmitHours,
    decimal RemainingAfterSubmitHours);

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
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal TotalHours,
    decimal PaidHours,
    decimal UnpaidHours,
    string Status,
    bool NoticePeriodMissed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
