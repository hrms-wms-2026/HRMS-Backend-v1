using ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.Mappers;

public static class LeaveCancellationMapper
{
    public static CancelLeaveRequestResponse ToResponse(
        LeaveRequest request,
        bool isPartialCancellation,
        decimal releasedPendingDays,
        decimal restoredUsedDays,
        decimal remainingDays,
        DateTimeOffset cancelledAt)
        => new(
            request.Id,
            request.Status,
            isPartialCancellation,
            request.PartialCancelEffectiveDate,
            releasedPendingDays,
            restoredUsedDays,
            remainingDays,
            request.CancellationReason,
            cancelledAt);
}
