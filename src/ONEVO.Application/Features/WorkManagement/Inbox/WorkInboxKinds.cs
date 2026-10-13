namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>How Work Management items are addressed in the Inbox: ("work", kind, id).</summary>
public static class WorkInboxKinds
{
    public const string Source = "work";
    public const string Approval = "approval";
    public const string Invitation = "invitation";
    public const string MonitorAlert = "monitor_alert";
}

public static class WorkInboxActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Cancel = "cancel";
    public const string Revert = "revert";
    public const string AcceptInvitation = "accept_invitation";
    public const string DeclineInvitation = "decline_invitation";
    /// <summary>Navigation only (the frontend opens the item's target); never executed server-side.</summary>
    public const string OpenTarget = "open_target";
}

public static class WorkInboxAlertStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}
