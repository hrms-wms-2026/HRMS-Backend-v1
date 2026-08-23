namespace ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

public record LeaveBalanceAuditResponse(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    string ChangeType,
    decimal DaysChanged,
    decimal BalanceAfter,
    string? Reason,
    Guid? RelatedRequestId,
    DateTimeOffset CreatedAt);
