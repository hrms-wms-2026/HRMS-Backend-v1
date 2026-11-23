namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Consumer for one outbox message type. Implementations must be safe to retry:
/// the worker redelivers on failure, so duplicate handling must be harmless.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>Message type key this handler consumes (see OutboxMessageTypes).</summary>
    string Type { get; }

    /// <summary>Handles the decrypted JSON payload of the message.</summary>
    Task HandleAsync(string payloadJson, CancellationToken ct);
}

/// <summary>Well-known outbox message type keys.</summary>
public static class OutboxMessageTypes
{
    public const string TenantOwnerInviteEmail = "tenant_owner_invite_email";
    public const string PasswordResetEmail = "password_reset_email";
    public const string AdminPasswordResetEmail = "admin_password_reset_email";
    public const string AdminPasswordChangedEmail = "admin_password_changed_email";

    // The four position types and EmployeeSecurityUpdated have only a no-op handler
    // (NoOpPositionOutboxHandler, NoOpEmployeeSecurityOutboxHandler): the messages are marked published
    // and nothing else happens. Whether to give them a consumer or stop writing them is open (task 31,
    // docs/reports/outbox/2026-11-20-outbox-guard-and-cleanup.md). Every type must keep exactly one
    // handler: OutboxHandlerMappingTests and OutboxHandlerStartupCheck enforce it.
    public const string PositionCreated = "position_created";
    public const string PositionUpdated = "position_updated";
    public const string PositionArchived = "position_archived";
    public const string PositionRestored = "position_restored";
    public const string PlatformManagerInviteEmail = "platform_manager_invite_email";
    public const string EmployeeOnboardingInviteEmail = "employee_onboarding_invite_email";
    public const string PositionChangeApprovalRequestEmail = "position_change_approval_request_email";

    // No-op handler only (see the note on the position types above).
    public const string EmployeeSecurityUpdated = "employee_security_updated";
    public const string InvoiceEmail = "invoice_email";
    public const string LeaveRequestApproved = "leave_request_approved";
    public const string LeaveRequestRejected = "leave_request_rejected";
    public const string LeaveRequestCancelled = "leave_request_cancelled";
    public const string WorkNotification = "work_notification";
    /// <summary>One channel delivery reserved by INotificationOrchestrator (NotificationDeliveryOutboxHandler).</summary>
    public const string NotificationDelivery = "notification_delivery";
    /// <summary>Wake one user's tray after a tray delivery commits (NotificationTrayPushOutboxHandler).</summary>
    public const string NotificationTrayPush = "notification_tray_push";
    /// <summary>Tells the affected people's open inboxes to refresh one item (InboxChangedOutboxHandler), after commit.</summary>
    public const string InboxChanged = "inbox_changed";
    public const string CalendarEventInviteEmail = "calendar_event_invite_email";
    /// <summary>Push one event to (or delete it from) the organizer's Outlook, which then emails the attendees.</summary>
    public const string CalendarOutlookPush = "calendar_outlook_push";
    /// <summary>Re-run the calendar meeting-exception rules for some employees (plan C2 triggers).</summary>
    public const string CalendarIncidentEvaluate = "calendar_incident_evaluate";
    public const string CalendarMeetingCancel = "calendar_meeting_cancel";
    public const string MeetingLinkProvision = "meeting_link_provision";
}
