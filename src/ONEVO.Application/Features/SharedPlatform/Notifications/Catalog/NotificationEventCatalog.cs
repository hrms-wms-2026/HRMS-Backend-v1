using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using MonitoringNotificationType = ONEVO.Domain.Features.Monitoring.Notifications.Entities.NotificationType;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;

/// <summary>
/// Every notification event the product can send, registered in code. Built from the migration
/// inventory (docs/notifications/event-inventory.md): the shared in-app template codes plus the
/// monitoring tray alerts. Platform/security emails (invites, password reset, invoices) are not
/// tenant-routable and are deliberately absent.
/// </summary>
public static class NotificationEventCatalog
{
    /// <summary>The first event delivered through the orchestrator on all three channels.</summary>
    public const string FirstSliceEventKey = "work_approval_requested";

    private const NotificationChannels AllChannels = NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Tray;

    public static readonly IReadOnlyList<NotificationEventDefinition> All =
    [
        Shared("work_task_creation_request_created", "work_management", "Work task creation request created", [V("requesterName"), V("taskTitle"), V("objectiveName")]),
        Shared("work_task_creation_request_decided", "work_management", "Work task creation request decided", [V("decision"), V("taskTitle"), V("objectiveName")]),
        Shared("work_task_edit_request_decided", "work_management", "Work task edit request decided", [V("decision"), V("taskTitle"), V("objectiveName")]),
        Shared("work_task_status_change_request_created", "work_management", "Work task status change request created", [V("requesterName"), V("projectName")]),
        Shared("work_task_status_change_request_decided", "work_management", "Work task status change request decided", [V("decision"), V("projectName")]),
        Shared("work_allocation_extend_request_created", "work_management", "Work allocation extend request created", [V("requesterName"), V("requestedHours"), V("objectiveName")]),
        Shared("work_allocation_extend_request_decided", "work_management", "Work allocation extend request decided", [V("decision"), V("objectiveName")]),
        Shared("work_objective_edit_request_created", "work_management", "Work objective edit request created", [V("requesterName"), V("objectiveName")]),
        Shared("work_objective_edit_request_decided", "work_management", "Work objective edit request decided", [V("decision"), V("objectiveName")]),
        Shared("work_sprint_completed", "work_management", "Work sprint completed", [V("sprintName"), V("objectiveName")]),
        Shared("work_sprint_incomplete", "work_management", "Work sprint incomplete", [V("sprintName"), V("objectiveName")]),
        Shared("work_sprint_achieved", "work_management", "Work sprint achieved", [V("sprintName"), V("objectiveName")]),
        Shared("work_activity_recorded", "work_management", "Work activity recorded", [V("actorName"), V("actionLabel"), V("targetTitle")]),
        Shared("work_approval_requested", "work_management", "Work approval requested", [V("actorName", required: true), V("actionLabel", required: true), V("targetTitle", required: true)]),
        Shared("work_approval_decided", "work_management", "Work approval decided", [V("decision"), V("actorName"), V("actionLabel"), V("targetTitle")]),
        Shared("work_monitor_alert", "work_management", "Work monitor alert", [V("actionLabel"), V("targetTitle")]),
        Shared("conversation_member_added", "conversations", "Conversation member added", [V("actorName"), V("itemTitle")]),
        Shared("conversation_mentioned", "conversations", "Conversation mentioned", [V("actorName"), V("itemTitle")]),
        Shared("leave_request_approved", "leave", "Leave request approved", [V("leaveTypeName"), V("startDate"), V("endDate")]),
        Shared("leave_request_rejected", "leave", "Leave request rejected", [V("leaveTypeName"), V("startDate"), V("endDate"), V("reason")]),
        Shared("leave_request_information_requested", "leave", "Leave request information requested", [V("approverName"), V("leaveTypeName"), V("startDate"), V("endDate")]),
        Shared("leave_request_next_approval_required", "leave", "Leave request next approval required", [V("employeeName"), V("leaveTypeName"), V("startDate"), V("endDate")]),
        Shared("leave_request_cancelled_by_employee", "leave", "Leave request cancelled by employee", [V("employeeName"), V("leaveTypeName"), V("startDate"), V("endDate")]),
        Shared("leave_request_cancelled_by_hr", "leave", "Leave request cancelled by hr", [V("cancelledByName"), V("leaveTypeName"), V("startDate"), V("endDate"), V("reason")]),
        Shared("leave_request_partially_cancelled", "leave", "Leave request partially cancelled", [V("leaveTypeName"), V("effectiveDate"), V("endDate"), V("restoredDays")]),
        Shared("face_verification_override", "monitoring", "Face verification override", [V("employeeName"), V("attempts"), V("action"), V("time"), V("reasons")]),
        Shared("exception_alert_detected", "monitoring", "Exception alert detected", [V("title"), V("employeeName"), V("description")]),
        Shared("exception_alert_escalated", "monitoring", "Exception alert escalated", [V("title"), V("employeeName"), V("description")]),
        Shared("attendance_correction_request_created", "time_attendance", "Attendance correction request created", [V("employeeName")]),
        Shared("attendance_correction_request_decided", "time_attendance", "Attendance correction request decided", [V("decision")]),
        Shared("attendance_correction_request_cancelled", "time_attendance", "Attendance correction request cancelled", []),
        Shared("work_area_change_request_created", "time_attendance", "Work area change request created", [V("employeeName"), V("requestedWorkArea"), V("date")]),
        Shared("work_area_change_request_decided", "time_attendance", "Work area change request decided", [V("decision"), V("date"), V("reviewComment")]),
        Shared("work_area_change_request_cancelled", "time_attendance", "Work area change request cancelled", [V("employeeName"), V("date")]),
        Shared("work_project_member_invited", "work_management", "Work project member invited", [V("inviterName"), V("projectName")]),
        Shared("work_objective_invitation_created", "work_management", "Work objective invitation created", [V("inviterName"), V("objectiveName"), V("inviteType")]),
        Shared("work_project_member_accepted", "work_management", "Work project member accepted", [V("accepterName"), V("projectName")]),
        Shared("calendar_event_participant_added", "calendar", "Calendar event participant added", [V("organizerName"), V("eventTitle"), V("eventDate")]),
        Shared("calendar_event_updated", "calendar", "Calendar event updated", [V("organizerName"), V("eventTitle")]),
        Shared("calendar_event_cancelled", "calendar", "Calendar event cancelled", [V("organizerName"), V("eventTitle")]),
        Shared("calendar_event_resolution_requested", "calendar", "Calendar event resolution requested", [V("responderName"), V("eventTitle"), V("reason")]),
        Shared("calendar_event_replacement_nominated", "calendar", "Calendar event replacement nominated", [V("responderName"), V("nomineeName"), V("eventTitle")]),
        Shared("calendar_event_rsvp_accepted", "calendar", "Calendar event rsvp accepted", [V("responderName"), V("eventTitle")]),
        Shared("calendar_event_rsvp_rejected", "calendar", "Calendar event rsvp rejected", [V("responderName"), V("eventTitle")]),
        Shared("calendar_event_replacement_nominee", "calendar", "Calendar event replacement nominee", [V("responderName"), V("eventTitle")]),
        Shared("calendar_event_participant_removed", "calendar", "Calendar event participant removed", [V("organizerName"), V("eventTitle")]),
        Shared("calendar_event_meeting_link_removed", "calendar", "Calendar event meeting link removed", [V("organizerName"), V("eventTitle")]),
        Shared("calendar_event_meeting_link_failed", "calendar", "Calendar event meeting link failed", [V("provider"), V("eventTitle")]),
        Shared("calendar_connection_reauth_required", "calendar", "Calendar connection reauth required", [V("provider")]),
        Shared("calendar_connection_failed", "calendar", "Calendar connection failed", [V("provider")]),
        Shared("calendar_connection_sync_conflict", "calendar", "Calendar connection sync conflict", [V("provider")]),
        Shared("calendar_event_reminder", "calendar", "Calendar event reminder", [V("eventTitle"), V("eventTime")]),
        Shared("calendar_exception_detected", "calendar", "Calendar exception detected", [V("employeeName"), V("title"), V("description")]),
        Shared("calendar_exception_raised_for_you", "calendar", "Calendar exception raised for you", [V("title"), V("description")]),
        Shared("calendar_exception_escalated", "calendar", "Calendar exception escalated", [V("title"), V("description")]),
        Shared("work_monitor_alert_escalated", "work_management", "Work monitor alert escalated", [V("title"), V("message")]),
        Shared("leave_request_decision_undone", "leave", "Leave request decision undone", [V("decision"), V("leaveType"), V("startDate")]),
        Monitoring(MonitoringNotificationType.BreakReminder, "Break reminder"),
        Monitoring(MonitoringNotificationType.LongIdleAlert, "Long idle alert"),
        Monitoring(MonitoringNotificationType.LowActivityAlert, "Low activity alert"),
        Monitoring(MonitoringNotificationType.FocusNudge, "Focus nudge"),
        Monitoring(MonitoringNotificationType.OutsideWorkLocationAlert, "Outside work location alert"),
        Monitoring(MonitoringNotificationType.BreakAllowanceExceeded, "Break allowance exceeded"),
        Monitoring(MonitoringNotificationType.IdentityVerificationAlert, "Identity verification alert"),
    ];

    private static readonly Dictionary<string, NotificationEventDefinition> ByKey =
        All.ToDictionary(e => e.Key, StringComparer.Ordinal);

    public static bool IsRegistered(string eventKey) => ByKey.ContainsKey(eventKey);

    public static bool TryGet(string eventKey, out NotificationEventDefinition definition)
        => ByKey.TryGetValue(eventKey, out definition!);

    public static NotificationEventDefinition Get(string eventKey)
        => ByKey.TryGetValue(eventKey, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Notification event '{eventKey}' is not registered.");

    /// <summary>Catalog key for a legacy monitoring alert type, e.g. <c>monitoring_long_idle_alert</c>.</summary>
    public static string MonitoringEventKey(MonitoringNotificationType type) => "monitoring_" + ToSnakeCase(type.ToString());

    /// <summary>An event the shared dispatcher already sends in-app. In-app stays the default; email and tray
    /// become available but start OFF.</summary>
    private static NotificationEventDefinition Shared(
        string key, string module, string displayName, IReadOnlyList<NotificationVariableDefinition> variables)
        => new(key, module, displayName,
            SupportedChannels: AllChannels,
            DefaultChannels: NotificationChannels.InApp,
            RequiredChannels: NotificationChannels.None,
            AllowedVariables: variables,
            AllowedRecipientRules: [NotificationRecipientRules.Default],
            AllowedActionKeys: [NotificationActionKeys.OpenRelated, NotificationActionKeys.None]);

    /// <summary>A monitoring alert produced straight into the monitoring notification table and polled by the
    /// tray. Tray-only until its producer is migrated (Task 8).</summary>
    private static NotificationEventDefinition Monitoring(MonitoringNotificationType type, string displayName)
        => new(MonitoringEventKey(type), "monitoring", displayName,
            SupportedChannels: NotificationChannels.Tray,
            DefaultChannels: NotificationChannels.Tray,
            RequiredChannels: NotificationChannels.None,
            AllowedVariables: [V("title"), V("message")],
            AllowedRecipientRules: [NotificationRecipientRules.Default],
            AllowedActionKeys: [NotificationActionKeys.None]);

    private static NotificationVariableDefinition V(string key, bool required = false) => new(key, "string", required);

    private static string ToSnakeCase(string pascal)
        => string.Concat(pascal.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
