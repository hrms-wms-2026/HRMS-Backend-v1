using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

public static class WorkNotificationKinds
{
    public const string Direct = "direct";
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";
    /// <summary>A project monitor alert (no approval request; the actor is the system, Guid.Empty).</summary>
    public const string Alert = "alert";
}

/// <summary>One recipient's copy of a Work Management activity - the project-scoped history shown
/// on the Approvals page. The in-app push itself goes through the WorkNotification outbox message.</summary>
public class WorkNotificationLog : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid RecipientEmployeeId { get; set; }
    public Guid ActorEmployeeId { get; set; }
    public string Kind { get; set; } = WorkNotificationKinds.Direct;
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string TargetTitle { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
}
