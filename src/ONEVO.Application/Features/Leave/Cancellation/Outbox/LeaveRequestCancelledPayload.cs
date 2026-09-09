using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Leave.Cancellation.Outbox;

public sealed record LeaveRequestCancelledPayload(
    Guid TenantId,
    Guid RequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    DateTimeOffset OriginalStartAt,
    DateTimeOffset OriginalEndAt,
    bool IsPartialCancellation,
    DateOnly? EffectiveDate,
    decimal ReleasedPendingHours,
    decimal RestoredPaidHours,
    decimal AffectedUnpaidHours,
    Guid CancelledByUserId,
    Guid CancelledByEmployeeId,
    bool CancelledByHr,
    string? Reason,
    DateTimeOffset CancelledAt);

public sealed class NoOpLeaveCancellationSideEffectOutboxHandler : IOutboxMessageHandler
{
    public string Type => OutboxMessageTypes.LeaveRequestCancelled;

    public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
}
