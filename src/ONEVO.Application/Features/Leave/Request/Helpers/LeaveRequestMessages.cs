namespace ONEVO.Application.Features.Leave.Request.Helpers;

public static class LeaveRequestMessages
{
    public const string EmployeeNotFound = "Employee was not found.";
    public const string NoEmployeeRecord = "No employee record is linked to the current user.";
    public const string StartInPast = "Leave start date cannot be in the past";
    public const string Overlap = "You already have a pending/approved leave request overlapping these dates";
    public const string Blackout = "The selected dates fall within a blackout period. Leave requests are restricted";
    public const string NoWorkingDays = "Leave request must include at least one working day.";
    public const string CrossYear = "Leave requests that span more than one year are not supported yet.";
    public const string WorkWindowRequired = "Set work start and end in General Settings first.";
    public const string EndAtNotAfterStartAt = "Leave end must be after leave start.";
    public const string NoApprover = "No approver found in your reporting line. Please contact HR";
    public const string GenderRestricted = "This leave type is not available for the employee's gender.";
    public const string NoEntitlement = "No active entitlement and policy were found for this employee, leave type, and year.";
    public const string FileNotAvailable = "A supporting document is not available for this request.";
    public const string RangeExceeded = "Leave request date range exceeds the configured maximum.";

    public static string InsufficientBalance(decimal remaining, string leaveTypeName) =>
        $"Insufficient balance. You have {remaining:0.#} hours remaining for {leaveTypeName}";

    public static string DocumentRequired(string leaveTypeName, int afterDays) =>
        $"A supporting document is required for {leaveTypeName} requests exceeding {afterDays} days";

    public static string NoticeMissed(int noticeDays) =>
        $"This request does not meet the minimum notice period of {noticeDays} days. Your manager will be informed.";

    public static string MaxConsecutive(int limit) =>
        $"Exceeds maximum consecutive days (policy limit: {limit})";

    public static string TeamAbsence(int count) =>
        $"{count} team members already on leave during this period";
}
