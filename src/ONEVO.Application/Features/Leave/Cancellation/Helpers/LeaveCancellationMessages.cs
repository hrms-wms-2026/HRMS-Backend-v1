namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public static class LeaveCancellationMessages
{
    public const string AlreadyCancelled = "This leave request has already been cancelled";
    public const string Rejected = "Rejected requests cannot be cancelled";
    public const string PeriodPassed = "This leave period has already passed and cannot be cancelled";
    public const string HrReasonRequired = "A reason is required when cancelling on behalf of an employee";
    public const string EmployeeReasonRequired = "A reason is required to cancel this leave request.";
    public const string Concurrency = "This request was modified by another user. Please refresh and try again";
    public const string NotOwner = "This request does not belong to you.";
    public const string NotCancellable = "This leave request cannot be cancelled in its current status.";
    public const string InvalidEffectiveDate = "Cancellation effective date must be within the leave request period.";
    public const string NoRestorableDays = "There are no future leave days to restore for this request.";
    public const string AllocationUnavailable =
        "This request cannot be partially cancelled because its day allocation history is unavailable.";
    public const string AuthRequired = "Authentication required.";
    public const string NoEmployee = "Employee profile was not found for the current user.";
    public const string NotFound = "Leave request was not found.";
}
