namespace ONEVO.Application.Features.Leave.Approval.OutboxHandlers;

public sealed record LeaveRequestApprovedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal PaidHours,
    decimal UnpaidHours,
    Guid ApprovedByEmployeeId);

public sealed record LeaveRequestRejectedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal PaidHours,
    decimal UnpaidHours,
    Guid RejectedByEmployeeId,
    string Reason);
