using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using CalendarCalendarEvent = ONEVO.Domain.Features.Calendar.Entities.CalendarEvent;
using CalendarCalendarEventStatuses = ONEVO.Domain.Features.Calendar.Entities.CalendarEventStatuses;
using WorkManagementCalendarEvent = ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities.CalendarEvent;
using WorkManagementCalendarEventStatuses = ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities.CalendarEventStatuses;
using MonitoringNotification = ONEVO.Domain.Features.Monitoring.Notifications.Entities.Notification;
using MonitoringNotificationType = ONEVO.Domain.Features.Monitoring.Notifications.Entities.NotificationType;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Infrastructure.Persistence.Integrity;

/// <summary>
/// CHECK (column IN (...)) constraints for status and type columns (task 39). The allowed values come from
/// the C# enum or constants class, so adding a value changes the model: a migration must follow, and
/// StatusCheckConstraintTests fails until it does. Columns still without a constraint are listed, with a
/// reason, in tests/ONEVO.Tests.Architecture/Schema/missing-check-constraints.txt.
/// </summary>
public static class StatusCheckConstraints
{
    /// <summary>One constrained column. <paramref name="ConstantPrefix"/> picks the constants of a class
    /// that also holds other constants (AttendanceRecord.StatusLate and so on).</summary>
    public sealed record Rule(Type Entity, string Table, string Column, Type Values, string? ConstantPrefix = null)
    {
        public string Name => $"ck_{Table}_{Column}";

        public IReadOnlyList<string> AllowedValues() => StatusCheckConstraints.AllowedValues(Values, ConstantPrefix);

        public string Sql() =>
            $"{Column} IN ({string.Join(", ", AllowedValues().Select(v => $"'{v.Replace("'", "''", StringComparison.Ordinal)}'"))})";
    }

    public static IReadOnlyList<Rule> Rules { get; } =
    [
        // Enum-backed columns (stored as the enum name).
        new(typeof(BiometricEnrollmentAttempt), "biometric_enrollment_attempts", "status", typeof(BiometricEnrollmentStatus)),
        new(typeof(BiometricProfile), "biometric_profiles", "status", typeof(BiometricProfileStatus)),
        new(typeof(MonitoringException), "exceptions", "status", typeof(ExceptionStatus)),
        new(typeof(MonitoringException), "exceptions", "type", typeof(ExceptionType)),
        new(typeof(MonitoringNotification), "monitoring_notifications", "type", typeof(MonitoringNotificationType)),
        new(typeof(Tenant), "tenants", "status", typeof(TenantStatus)),
        new(typeof(TenantStatusHistory), "tenant_status_histories", "from_status", typeof(TenantStatus)),
        new(typeof(TenantStatusHistory), "tenant_status_histories", "to_status", typeof(TenantStatus)),

        // Constants-backed columns: the tables named in task 39, then core HR, leave, attendance and work.
        new(typeof(LeaveRequest), "leave_requests", "status", typeof(LeaveRequestStatuses)),
        new(typeof(AttendanceRecord), "attendance_records", "status", typeof(AttendanceRecord), "Status"),
        new(typeof(OutboxMessage), "outbox_messages", "status", typeof(OutboxMessageStatus)),
        new(typeof(LeaveRequestApprover), "leave_request_approvers", "status", typeof(LeaveRequestApproverStatuses)),
        new(typeof(LeaveRequestDayAllocation), "leave_request_day_allocations", "status", typeof(LeaveRequestDayAllocationStatuses)),
        new(typeof(LeaveBalanceAudit), "leave_balance_audits", "change_type", typeof(LeaveBalanceChangeTypes)),
        new(typeof(AttendanceAlert), "attendance_alerts", "status", typeof(AttendanceAlertStatuses)),
        new(typeof(AttendanceAlertDeduction), "attendance_alert_deductions", "status", typeof(AttendanceAlertDeductionStatuses)),
        new(typeof(PositionAssignment), "position_assignments", "assignment_status", typeof(PositionAssignmentStatus)),
        new(typeof(OnboardingDraft), "onboarding_drafts", "status", typeof(OnboardingDraftStatus)),
        new(typeof(BulkOnboardingBatch), "bulk_onboarding_batches", "status", typeof(BulkOnboardingBatchStatus)),
        new(typeof(BulkOnboardingBatchRow), "bulk_onboarding_batch_rows", "status", typeof(BulkOnboardingBatchRowStatus)),
        new(typeof(Sprint), "sprints", "status", typeof(SprintStatuses)),

        // Task 39f: the review workflow tables of task 37.
        new(typeof(AccessGrantRequest), "access_grant_requests", "action_type", typeof(AccessGrantActionType)),
        new(typeof(AccessGrantRequest), "access_grant_requests", "approval_status", typeof(AccessGrantApprovalStatuses)),
        new(typeof(AttendanceCorrection), "attendance_corrections", "correction_type", typeof(AttendanceCorrection), "Type"),
        new(typeof(AttendanceCorrection), "attendance_corrections", "status", typeof(AttendanceCorrection), "Status"),
        new(typeof(LocationChangeRequest), "location_change_requests", "status", typeof(LocationChangeRequest), "Status"),
        new(typeof(WorkAreaChangeRequest), "work_area_change_requests", "status", typeof(WorkAreaChangeRequest), "Status"),
        new(typeof(DeviceChangeRequest), "device_change_requests", "status", typeof(DeviceChangeRequest), "Status"),
        new(typeof(OffboardingTaskBypassRequest), "offboarding_task_bypass_requests", "status", typeof(BypassRequestStatuses)),
        new(typeof(OffboardingTaskBypassRequest), "offboarding_task_bypass_requests", "prior_task_status", typeof(EmployeeChecklistTaskStatuses)),
        new(typeof(SeatIncreaseRequest), "seat_increase_requests", "status", typeof(SeatIncreaseRequestStatus)),
        new(typeof(WorkApprovalRequest), "wm_approval_requests", "action_type", typeof(WorkActionTypes)),
        new(typeof(WorkApprovalRequest), "wm_approval_requests", "status", typeof(WorkApprovalRequestStatuses)),
        new(typeof(ProjectMemberInvitation), "project_member_invitations", "invite_type", typeof(ProjectInvitationTypes)),
        new(typeof(ProjectMemberInvitation), "project_member_invitations", "status", typeof(ProjectInvitationStatuses)),

        // Task 39f: type columns next to polymorphic ids.
        new(typeof(WorkApprovalRequest), "wm_approval_requests", "target_type", typeof(WorkTargetTypes)),
        new(typeof(MonitorAlert), "wm_monitor_alerts", "target_type", typeof(MonitorTargetTypes)),
        new(typeof(WorkNotificationLog), "wm_notification_log", "kind", typeof(WorkNotificationKinds)),
        new(typeof(EntityAsset), "entity_assets", "owner_type", typeof(EntityAssetOwnerTypes)),

        // Task 39f: the rest.
        new(typeof(CalendarEventMeeting), "calendar_event_meetings", "owner_kind", typeof(CalendarMeetingOwnerKinds)),
        new(typeof(CalendarEventMeeting), "calendar_event_meetings", "status", typeof(CalendarEventMeetingStatuses)),
        new(typeof(CalendarEventParticipant), "calendar_event_participants", "response_status", typeof(CalendarEventParticipantStatuses)),
        new(typeof(WorkManagementCalendarEvent), "calendar_events", "status", typeof(WorkManagementCalendarEventStatuses)),
        new(typeof(ClockInPolicy), "clock_in_policies", "scope_type", typeof(ClockInPolicy), "Scope"),
        new(typeof(ConversationMessageRevision), "conversation_message_revisions", "kind", typeof(ConversationRevisionKinds)),
        new(typeof(ConversationMessage), "conversation_messages", "kind", typeof(ConversationMessageKinds)),
        new(typeof(EmployeeChecklistTask), "employee_checklist_tasks", "status", typeof(EmployeeChecklistTaskStatuses)),
        new(typeof(EmployeeChecklistTask), "employee_checklist_tasks", "owner_type", typeof(ChecklistTaskOwnerTypes)),
        new(typeof(ExternalCalendarConnection), "external_calendar_connections", "status", typeof(ExternalCalendarConnectionStatuses)),
        new(typeof(ExternalCalendarEventLink), "external_calendar_event_links", "sync_status", typeof(ExternalCalendarSyncStatuses)),
        new(typeof(FileRecord), "file_records", "status", typeof(FileRecordStatus)),
        new(typeof(FileUploadReservation), "file_upload_reservations", "status", typeof(FileUploadReservationStatus)),
        new(typeof(IdempotencyRecord), "idempotency_records", "status", typeof(IdempotencyRecordStatus)),
        new(typeof(LeavePolicyAssignment), "leave_policy_assignments", "target_kind", typeof(LeavePolicyTargetKinds)),
        new(typeof(ManagementCoverageRecord), "management_coverage_records", "covered_target_type", typeof(ManagementCoverageRecord), "Target"),
        new(typeof(ManagementCoverageRecord), "management_coverage_records", "status", typeof(ManagementCoverageRecord), "Status"),
        new(typeof(MeetingDetails), "meeting_details", "attendance_status", typeof(MeetingAttendanceStatuses)),
        new(typeof(MeetingOccurrenceAttendee), "meeting_occurrence_attendees", "attendee_kind", typeof(MeetingAttendeeKinds)),
        new(typeof(MonitoringFaceScan), "monitoring_face_scans", "status", typeof(MonitoringFaceScanStatus)),
        new(typeof(NotificationDelivery), "notification_deliveries", "status", typeof(NotificationDeliveryStatus)),
        new(typeof(NotificationTemplateVersion), "notification_template_versions", "status", typeof(NotificationTemplateVersionStatus)),
        new(typeof(OffboardingRecord), "offboarding_records", "status", typeof(OffboardingRecordStatuses)),
        new(typeof(PermissionChange), "permission_changes", "kind", typeof(PermissionChangeKinds)),
        new(typeof(CalendarCalendarEvent), "personal_calendar_events", "event_status", typeof(CalendarCalendarEventStatuses)),
        new(typeof(CalendarCalendarEvent), "personal_calendar_events", "source_type", typeof(CalendarEventSourceTypes)),
        new(typeof(PlatformAuthEvent), "platform_auth_events", "event_type", typeof(PlatformAuthEvent)),
        new(typeof(PlatformUser), "platform_users", "invite_status", typeof(PlatformUser), "Invite"),
        new(typeof(PlatformUser), "platform_users", "mfa_status", typeof(PlatformUser), "Mfa"),
        new(typeof(PlatformUser), "platform_users", "status", typeof(PlatformUser), "Status"),
        new(typeof(PositionAssignment), "position_assignments", "assignment_kind", typeof(PositionAssignmentKind)),
        new(typeof(Position), "positions", "position_type", typeof(Position), "Type"),
        new(typeof(ReleaseCalendarEntry), "release_calendar", "reminder_type", typeof(ReleaseReminderTypes)),
        new(typeof(SprintActivityLog), "sprint_activity_logs", "from_status", typeof(SprintStatuses)),
        new(typeof(SprintActivityLog), "sprint_activity_logs", "to_status", typeof(SprintStatuses)),
        new(typeof(SupportTicket), "support_tickets", "status", typeof(SupportTicket), "Status"),
        new(typeof(TaskCategory), "task_categories", "kind", typeof(TaskCategoryKinds)),
        new(typeof(WorkTask), "tasks", "task_kind", typeof(WorkTaskKinds)),
        new(typeof(AgentCommand), "agent_commands", "command_type", typeof(AgentCommandTypes)),
        new(typeof(AgentCommand), "agent_commands", "status", typeof(AgentCommandStatuses)),
        new(typeof(MonitoringEvidenceAsset), "monitoring_evidence_assets", "evidence_type", typeof(MonitoringEvidenceTypes)),
        new(typeof(MonitoringEvidenceAsset), "monitoring_evidence_assets", "trigger_type", typeof(MonitoringEvidenceTriggers)),
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var rule in Rules)
        {
            modelBuilder.Entity(rule.Entity).ToTable(table => table.HasCheckConstraint(rule.Name, rule.Sql()));
        }
    }

    /// <summary>Enum names in declaration order, or the class's public string constants in declaration order.</summary>
    public static IReadOnlyList<string> AllowedValues(Type values, string? constantPrefix = null)
    {
        if (values.IsEnum)
        {
            return Enum.GetNames(values);
        }

        return values.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Where(field => constantPrefix is null || field.Name.StartsWith(constantPrefix, StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
