namespace ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;

public sealed record CancelLeaveRequestResponse(
    Guid RequestId,
    string Status,
    bool IsPartialCancellation,
    DateOnly? EffectiveDate,
    decimal ReleasedPendingDays,
    decimal RestoredUsedDays,
    decimal RemainingDays,
    string? Reason,
    DateTimeOffset CancelledAt);
