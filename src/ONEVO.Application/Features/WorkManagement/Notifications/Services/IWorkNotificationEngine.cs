namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

/// <summary>Kind is a WorkNotificationKinds value. ApprovalRequestId is set for every kind except Direct.
/// MonitorAlertId is set for Alert, so the alert's Inbox item can be refreshed.</summary>
public sealed record WorkNotificationEvent(
    Guid TenantId,
    Guid ProjectId,
    Guid ActorEmployeeId,
    string Kind,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid? ApprovalRequestId,
    IReadOnlyCollection<Guid> RecipientEmployeeIds,
    Guid? MonitorAlertId = null);

/// <summary>
/// The Work Management notification engine: one wm_notification_log row plus one WorkNotification
/// outbox message per recipient. The actor, duplicates and inactive employees are skipped. Never
/// calls SaveChangesAsync - callers run it inside the same transaction as the change it describes,
/// so a rollback also drops the notification.
/// </summary>
public interface IWorkNotificationEngine
{
    Task NotifyAsync(WorkNotificationEvent notification, CancellationToken ct = default);

    /// <summary>Tells these people's open Inbox to refresh one Work item ("approval", "invitation",
    /// "monitor_alert"). Enqueued in the caller's transaction and pushed after commit. NotifyAsync already
    /// does this for approval and alert events; call it directly only for changes that notify nobody.</summary>
    Task InboxChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, string kind, Guid id, CancellationToken ct = default);
}
