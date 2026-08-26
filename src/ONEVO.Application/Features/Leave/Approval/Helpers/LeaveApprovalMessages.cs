namespace ONEVO.Application.Features.Leave.Approval.Helpers;

public static class LeaveApprovalMessages
{
    public const string AuthRequired = "Authentication required.";
    public const string TenantMissing = "Tenant context missing.";
    public const string NoEmployee = "No employee record is linked to the current user.";
    public const string NotFound = "Leave request was not found.";
    public const string AlreadyFinal = "This request has already been approved or rejected";
    public const string NotAssigned = "This request is not assigned to you";
    public const string SelfApproval = "You cannot approve your own leave request.";
    public const string MissingPolicy = "No active leave policy was found for this employee's legal entity.";
    public const string NotYours = "This request does not belong to you.";
    public const string NotWaitingInfo = "This leave request is not waiting for more information.";
    public const string NoPausedApprover = "No paused approver was found for this request.";
    public const string FileNotAvailable = "A supporting document is not available for this request.";

    public static string BalanceChanged(decimal remaining) =>
        $"Employee's balance has changed since submission. Current balance: {remaining:0.#} days.";
}
