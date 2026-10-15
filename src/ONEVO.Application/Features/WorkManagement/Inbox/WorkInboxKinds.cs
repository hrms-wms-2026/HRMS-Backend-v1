using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>How Work Management items are addressed in the Inbox: ("work", kind, id).</summary>
public static class WorkInboxKinds
{
    public const string Source = "work";
    public const string Approval = "approval";
    public const string Invitation = "invitation";
    public const string MonitorAlert = "monitor_alert";

    /// <summary>The Work kinds in the Inbox registry (plan W1). Approvals keep their own WM revert action and window
    /// (ApprovalRevertWindow = the standard 30 minutes), so they do not declare the standard Revert capability.</summary>
    public static readonly IReadOnlyList<InboxKindDescriptor> Descriptors =
    [
        new(Source, Approval, "Work approval", InboxCategories.Request,
            InboxCapabilities.Chat | InboxCapabilities.EditBeforeApprove | InboxCapabilities.Cancel),
        new(Source, Invitation, "Module invitation", InboxCategories.Request, InboxCapabilities.Chat | InboxCapabilities.Cancel),
        new(Source, MonitorAlert, "Project monitor alert", InboxCategories.Alert, InboxCapabilities.Chat | InboxCapabilities.Escalate),
    ];
}

public static class WorkInboxActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Cancel = "cancel";
    public const string Revert = "revert";
    public const string Escalate = "escalate";
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
