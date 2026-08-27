using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public enum LeaveCancellationKind
{
    PendingStyle,
    ApprovedFull,
    ApprovedPartial
}

public sealed record LeaveCancellationClassification(
    LeaveCancellationKind Kind,
    DateOnly BusinessDate,
    DateOnly? EffectiveDate);

public sealed class LeaveCancellationClassifier
{
    public Result<LeaveCancellationClassification> Classify(
        string status,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly businessDate,
        DateOnly? requestedEffectiveDate)
    {
        if (status == LeaveRequestStatuses.Cancelled)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.AlreadyCancelled);

        if (status == LeaveRequestStatuses.Rejected)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.Rejected);

        if (endDate < businessDate)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.PeriodPassed);

        if (status is LeaveRequestStatuses.Pending or LeaveRequestStatuses.InformationRequested)
        {
            return Result<LeaveCancellationClassification>.Success(
                new LeaveCancellationClassification(LeaveCancellationKind.PendingStyle, businessDate, null));
        }

        if (status != LeaveRequestStatuses.Approved)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.NotCancellable);

        if (requestedEffectiveDate is { } supplied
            && (supplied < startDate || supplied > endDate))
        {
            return Result<LeaveCancellationClassification>.Failure(LeaveCancellationMessages.InvalidEffectiveDate);
        }

        if (businessDate <= startDate)
        {
            return Result<LeaveCancellationClassification>.Success(
                new LeaveCancellationClassification(LeaveCancellationKind.ApprovedFull, businessDate, null));
        }

        var effectiveDate = requestedEffectiveDate ?? businessDate;
        if (effectiveDate < businessDate)
            effectiveDate = businessDate;

        return Result<LeaveCancellationClassification>.Success(
            new LeaveCancellationClassification(LeaveCancellationKind.ApprovedPartial, businessDate, effectiveDate));
    }
}
