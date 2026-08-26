namespace ONEVO.Application.Features.Leave.Approval.DTOs.Responses;

public sealed record LeaveApprovalDecisionResponse(
    Guid RequestId,
    string Status,
    string CurrentApproverState,
    decimal PaidDaysMovedFromPending,
    decimal UnpaidDays,
    decimal RemainingDays,
    IReadOnlyList<LeaveApprovalWarningResponse> CurrentWarnings);

public sealed record LeaveApprovalWarningResponse(string Code, string Message);

public sealed record LeavePendingApprovalListItemResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record LeaveRequestAllListItemResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record LeaveApprovalDetailResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    string? Reason,
    IReadOnlyList<LeaveApprovalApproverResponse> Approvers,
    IReadOnlyList<LeaveApprovalInfoMessageResponse> InfoMessages,
    string? SubmissionConflictSnapshotJson,
    IReadOnlyList<LeaveApprovalWarningResponse> CurrentWarnings,
    decimal RemainingDays);

public sealed record LeaveApprovalApproverResponse(
    Guid ApproverEmployeeId,
    int SequenceOrder,
    string Status,
    string? Comment,
    Guid? DelegatedFromApproverId,
    DateTimeOffset? DecidedAt);

public sealed record LeaveApprovalInfoMessageResponse(
    Guid SenderEmployeeId,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record LeaveApprovalBulkResultResponse(
    IReadOnlyList<LeaveApprovalBulkItemResponse> Items,
    int SuccessCount,
    int FailureCount);

public sealed record LeaveApprovalBulkItemResponse(
    Guid RequestId,
    bool Success,
    string? Status,
    string? Error);
