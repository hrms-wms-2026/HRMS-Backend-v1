namespace ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;

public sealed record CancelLeaveRequestResponse(
    Guid RequestId,
    string Status,
    bool IsPartialCancellation,
    DateOnly? EffectiveDate,
    decimal ReleasedPendingHours,
    decimal RestoredUsedHours,
    decimal RemainingHours,
    string? Reason,
    DateTimeOffset CancelledAt);
