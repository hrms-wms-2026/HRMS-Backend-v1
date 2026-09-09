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

public sealed record LeaveInformationRequestedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    Guid RequestedByEmployeeId,
    string Question);

public sealed class NoOpLeaveApprovalSideEffectOutboxHandler : ONEVO.Application.Common.ServiceInterfaces.IOutboxMessageHandler
{
    public NoOpLeaveApprovalSideEffectOutboxHandler(string type) => Type = type;

    public string Type { get; }

    public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
}
