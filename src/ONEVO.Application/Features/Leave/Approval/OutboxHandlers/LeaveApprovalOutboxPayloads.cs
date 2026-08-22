namespace ONEVO.Application.Features.Leave.Approval.OutboxHandlers;

public sealed record LeaveRequestApprovedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PaidDays,
    decimal UnpaidDays,
    Guid ApprovedByEmployeeId);

public sealed record LeaveRequestRejectedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PaidDays,
    decimal UnpaidDays,
    Guid RejectedByEmployeeId,
    string Reason);

public sealed record LeaveInformationRequestedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid RequestedByEmployeeId,
    string Question);

public sealed class NoOpLeaveApprovalSideEffectOutboxHandler : ONEVO.Application.Common.ServiceInterfaces.IOutboxMessageHandler
{
    public NoOpLeaveApprovalSideEffectOutboxHandler(string type) => Type = type;

    public string Type { get; }

    public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
}
