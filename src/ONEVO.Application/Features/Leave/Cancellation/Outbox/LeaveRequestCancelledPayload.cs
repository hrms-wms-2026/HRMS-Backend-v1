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
