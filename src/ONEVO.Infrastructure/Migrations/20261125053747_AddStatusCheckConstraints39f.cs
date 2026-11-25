using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusCheckConstraints39f : Migration
    {
        /// <summary>
        /// Hand-written (task 39f). Read-only: runs before the constraints, in the admin tenant mode (RLS applies to sa), and
        /// stops the migration with 50039 naming each column that holds a value outside its new list. It changes no data.
        /// </summary>
        public const string CheckStatusValuesSql = """
        SET NOCOUNT ON;
        EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';
        DECLARE @n int, @bad nvarchar(max) = N'';
        -- A row whose value is outside its new list stops the migration and is named here. Nothing is changed.
        -- NULL passes a CHECK constraint, so NOT (...) leaves NULL rows out.
        SELECT @n = COUNT(*) FROM work_area_change_requests WHERE NOT (status IN ('pending', 'approved', 'rejected', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' work_area_change_requests.status=', @n);
        SELECT @n = COUNT(*) FROM wm_notification_log WHERE NOT (kind IN ('direct', 'requested', 'approved', 'rejected', 'cancelled', 'stale', 'commented', 'alert', 'reverted'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' wm_notification_log.kind=', @n);
        SELECT @n = COUNT(*) FROM wm_monitor_alerts WHERE NOT (target_type IN ('module', 'sprint', 'task'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' wm_monitor_alerts.target_type=', @n);
        SELECT @n = COUNT(*) FROM wm_approval_requests WHERE NOT (action_type IN ('task.create', 'meeting.create', 'meeting.move', 'task.edit', 'task.delete', 'task.status_change', 'task.comment', 'module.edit', 'module.delete', 'module.transfer', 'module.achieve', 'module.unachieve', 'module.allocation_extend', 'module.member_add', 'module.member_remove', 'sprint.create', 'sprint.edit', 'sprint.delete', 'sprint.start', 'sprint.complete', 'sprint.achieve', 'project.status_template_change'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' wm_approval_requests.action_type=', @n);
        SELECT @n = COUNT(*) FROM wm_approval_requests WHERE NOT (status IN ('pending', 'approved', 'rejected', 'cancelled', 'stale'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' wm_approval_requests.status=', @n);
        SELECT @n = COUNT(*) FROM wm_approval_requests WHERE NOT (target_type IN ('module', 'task', 'sprint', 'project', 'meeting'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' wm_approval_requests.target_type=', @n);
        SELECT @n = COUNT(*) FROM tasks WHERE NOT (task_kind IN ('standard', 'employee_checklist', 'meeting'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' tasks.task_kind=', @n);
        SELECT @n = COUNT(*) FROM task_categories WHERE NOT (kind IN ('task', 'bug', 'story', 'feature', 'testing', 'meeting', 'checklist'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' task_categories.kind=', @n);
        SELECT @n = COUNT(*) FROM support_tickets WHERE NOT (status IN ('open', 'in_progress', 'resolved', 'closed'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' support_tickets.status=', @n);
        SELECT @n = COUNT(*) FROM sprint_activity_logs WHERE NOT (from_status IN ('draft', 'active', 'complete', 'achieved'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' sprint_activity_logs.from_status=', @n);
        SELECT @n = COUNT(*) FROM sprint_activity_logs WHERE NOT (to_status IN ('draft', 'active', 'complete', 'achieved'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' sprint_activity_logs.to_status=', @n);
        SELECT @n = COUNT(*) FROM seat_increase_requests WHERE NOT (status IN ('pending', 'approved', 'rejected'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' seat_increase_requests.status=', @n);
        SELECT @n = COUNT(*) FROM release_calendar WHERE NOT (reminder_type IN ('project_release'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' release_calendar.reminder_type=', @n);
        SELECT @n = COUNT(*) FROM project_member_invitations WHERE NOT (invite_type IN ('member', 'leader'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' project_member_invitations.invite_type=', @n);
        SELECT @n = COUNT(*) FROM project_member_invitations WHERE NOT (status IN ('pending', 'accepted', 'declined', 'expired', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' project_member_invitations.status=', @n);
        SELECT @n = COUNT(*) FROM positions WHERE NOT (position_type IN ('unique', 'pooled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' positions.position_type=', @n);
        SELECT @n = COUNT(*) FROM position_assignments WHERE NOT (assignment_kind IN ('PrimaryEmployment', 'AdditionalAuthority'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' position_assignments.assignment_kind=', @n);
        SELECT @n = COUNT(*) FROM platform_users WHERE NOT (invite_status IN ('pending', 'accepted', 'revoked', 'expired'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' platform_users.invite_status=', @n);
        SELECT @n = COUNT(*) FROM platform_users WHERE NOT (mfa_status IN ('not_enrolled', 'enrolled', 'locked'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' platform_users.mfa_status=', @n);
        SELECT @n = COUNT(*) FROM platform_users WHERE NOT (status IN ('pending', 'active', 'inactive'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' platform_users.status=', @n);
        SELECT @n = COUNT(*) FROM platform_auth_events WHERE NOT (event_type IN ('login_succeeded', 'login_failed', 'session_revoked', 'logout_succeeded', 'admin_password_reset_requested', 'admin_password_reset_completed', 'platform_manager_invited', 'platform_manager_invite_accepted', 'mfa_reset'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' platform_auth_events.event_type=', @n);
        SELECT @n = COUNT(*) FROM personal_calendar_events WHERE NOT (event_status IN ('confirmed', 'tentative', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' personal_calendar_events.event_status=', @n);
        SELECT @n = COUNT(*) FROM personal_calendar_events WHERE NOT (source_type IN ('manual', 'external_sync', 'holiday'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' personal_calendar_events.source_type=', @n);
        SELECT @n = COUNT(*) FROM permission_changes WHERE NOT (kind IN ('rename', 'default-grant', 'soft-retire', 'remove'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' permission_changes.kind=', @n);
        SELECT @n = COUNT(*) FROM offboarding_task_bypass_requests WHERE NOT (prior_task_status IN ('pending', 'in_progress', 'completed', 'bypassed'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' offboarding_task_bypass_requests.prior_task_status=', @n);
        SELECT @n = COUNT(*) FROM offboarding_task_bypass_requests WHERE NOT (status IN ('pending', 'approved', 'rejected', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' offboarding_task_bypass_requests.status=', @n);
        SELECT @n = COUNT(*) FROM offboarding_records WHERE NOT (status IN ('initiated', 'in_progress', 'completed', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' offboarding_records.status=', @n);
        SELECT @n = COUNT(*) FROM notification_template_versions WHERE NOT (status IN ('draft', 'published'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' notification_template_versions.status=', @n);
        SELECT @n = COUNT(*) FROM notification_deliveries WHERE NOT (status IN ('pending', 'provider_accepted', 'available_in_inbox', 'forwarded_to_tray', 'client_accepted', 'skipped', 'expired', 'failed'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' notification_deliveries.status=', @n);
        SELECT @n = COUNT(*) FROM monitoring_face_scans WHERE NOT (status IN ('pending_scan', 'available', 'failed', 'verified', 'not_matched', 'no_reference_photo'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' monitoring_face_scans.status=', @n);
        SELECT @n = COUNT(*) FROM monitoring_evidence_assets WHERE NOT (evidence_type IN ('screenshot'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' monitoring_evidence_assets.evidence_type=', @n);
        SELECT @n = COUNT(*) FROM monitoring_evidence_assets WHERE NOT (trigger_type IN ('on_demand', 'periodic', 'inactivity_approved'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' monitoring_evidence_assets.trigger_type=', @n);
        SELECT @n = COUNT(*) FROM meeting_occurrence_attendees WHERE NOT (attendee_kind IN ('employee', 'guest'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' meeting_occurrence_attendees.attendee_kind=', @n);
        SELECT @n = COUNT(*) FROM meeting_details WHERE NOT (attendance_status IN ('pending', 'provisional', 'synced', 'attendance_unavailable', 'sync_failed', 'not_applicable'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' meeting_details.attendance_status=', @n);
        SELECT @n = COUNT(*) FROM management_coverage_records WHERE NOT (covered_target_type IN ('Position', 'Department', 'Company'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' management_coverage_records.covered_target_type=', @n);
        SELECT @n = COUNT(*) FROM management_coverage_records WHERE NOT (status IN ('active', 'inactive'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' management_coverage_records.status=', @n);
        SELECT @n = COUNT(*) FROM location_change_requests WHERE NOT (status IN ('pending', 'approved', 'rejected', 'cancelled', 'applied'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' location_change_requests.status=', @n);
        SELECT @n = COUNT(*) FROM leave_policy_assignments WHERE NOT (target_kind IN ('legal_entity', 'department', 'position', 'employee'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' leave_policy_assignments.target_kind=', @n);
        SELECT @n = COUNT(*) FROM idempotency_records WHERE NOT (status IN ('in_progress', 'completed'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' idempotency_records.status=', @n);
        SELECT @n = COUNT(*) FROM file_upload_reservations WHERE NOT (status IN ('active', 'completed', 'expired', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' file_upload_reservations.status=', @n);
        SELECT @n = COUNT(*) FROM file_records WHERE NOT (status IN ('pending_scan', 'available', 'quarantined', 'deleted'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' file_records.status=', @n);
        SELECT @n = COUNT(*) FROM external_calendar_event_links WHERE NOT (sync_status IN ('synced', 'pending', 'failed', 'skipped', 'conflict'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' external_calendar_event_links.sync_status=', @n);
        SELECT @n = COUNT(*) FROM external_calendar_connections WHERE NOT (status IN ('active', 'reauth_required', 'paused', 'revoked', 'failed'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' external_calendar_connections.status=', @n);
        SELECT @n = COUNT(*) FROM entity_assets WHERE NOT (owner_type IN ('project', 'objective', 'task', 'comment', 'employee', 'legal_entity', 'employee_document', 'checklist_template', 'conversation'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' entity_assets.owner_type=', @n);
        SELECT @n = COUNT(*) FROM employee_checklist_tasks WHERE NOT (owner_type IN ('employee', 'manager', 'hr', 'it', 'custom_user'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' employee_checklist_tasks.owner_type=', @n);
        SELECT @n = COUNT(*) FROM employee_checklist_tasks WHERE NOT (status IN ('pending', 'in_progress', 'completed', 'bypassed'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' employee_checklist_tasks.status=', @n);
        SELECT @n = COUNT(*) FROM device_change_requests WHERE NOT (status IN ('pending', 'approved', 'rejected'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' device_change_requests.status=', @n);
        SELECT @n = COUNT(*) FROM conversation_messages WHERE NOT (kind IN ('text', 'system', 'evidence', 'case'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' conversation_messages.kind=', @n);
        SELECT @n = COUNT(*) FROM conversation_message_revisions WHERE NOT (kind IN ('edit', 'delete'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' conversation_message_revisions.kind=', @n);
        SELECT @n = COUNT(*) FROM clock_in_policies WHERE NOT (scope_type IN ('full_company', 'department', 'position', 'employee'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' clock_in_policies.scope_type=', @n);
        SELECT @n = COUNT(*) FROM calendar_events WHERE NOT (status IN ('active', 'archived'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' calendar_events.status=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_participants WHERE NOT (response_status IN ('pending', 'accepted', 'rejected', 'resolution_requested', 'replacement_nominated'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' calendar_event_participants.response_status=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_meetings WHERE NOT (owner_kind IN ('calendar_event', 'meeting_series'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' calendar_event_meetings.owner_kind=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_meetings WHERE NOT (status IN ('active', 'cancelled', 'failed', 'provisioning', 'provisioning_unknown', 'unresolved'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' calendar_event_meetings.status=', @n);
        SELECT @n = COUNT(*) FROM attendance_corrections WHERE NOT (correction_type IN ('clock_in', 'clock_out', 'break', 'full_day'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' attendance_corrections.correction_type=', @n);
        SELECT @n = COUNT(*) FROM attendance_corrections WHERE NOT (status IN ('pending', 'approved', 'rejected', 'cancelled'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' attendance_corrections.status=', @n);
        SELECT @n = COUNT(*) FROM agent_commands WHERE NOT (command_type IN ('capture_screenshot'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' agent_commands.command_type=', @n);
        SELECT @n = COUNT(*) FROM agent_commands WHERE NOT (status IN ('pending', 'delivered', 'completed', 'failed', 'expired'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' agent_commands.status=', @n);
        SELECT @n = COUNT(*) FROM access_grant_requests WHERE NOT (action_type IN ('onboarding_position_access', 'position_change_access'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' access_grant_requests.action_type=', @n);
        SELECT @n = COUNT(*) FROM access_grant_requests WHERE NOT (approval_status IN ('Pending', 'Approved', 'Rejected'));
        IF @n > 0 SET @bad = CONCAT(@bad, N' access_grant_requests.approval_status=', @n);
        EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
        IF @bad <> N'' BEGIN DECLARE @msg nvarchar(2048) = LEFT(CONCAT(N'Task 39f: rows with a value outside the new check constraint:', @bad, N'. Report them; the migration changes no data.'), 2048); THROW 50039, @msg, 1; END
        """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CheckStatusValuesSql);

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_area_change_requests_status",
                table: "work_area_change_requests",
                sql: "status IN ('pending', 'approved', 'rejected', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wm_notification_log_kind",
                table: "wm_notification_log",
                sql: "kind IN ('direct', 'requested', 'approved', 'rejected', 'cancelled', 'stale', 'commented', 'alert', 'reverted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wm_monitor_alerts_target_type",
                table: "wm_monitor_alerts",
                sql: "target_type IN ('module', 'sprint', 'task')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wm_approval_requests_action_type",
                table: "wm_approval_requests",
                sql: "action_type IN ('task.create', 'meeting.create', 'meeting.move', 'task.edit', 'task.delete', 'task.status_change', 'task.comment', 'module.edit', 'module.delete', 'module.transfer', 'module.achieve', 'module.unachieve', 'module.allocation_extend', 'module.member_add', 'module.member_remove', 'sprint.create', 'sprint.edit', 'sprint.delete', 'sprint.start', 'sprint.complete', 'sprint.achieve', 'project.status_template_change')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wm_approval_requests_status",
                table: "wm_approval_requests",
                sql: "status IN ('pending', 'approved', 'rejected', 'cancelled', 'stale')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wm_approval_requests_target_type",
                table: "wm_approval_requests",
                sql: "target_type IN ('module', 'task', 'sprint', 'project', 'meeting')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_task_kind",
                table: "tasks",
                sql: "task_kind IN ('standard', 'employee_checklist', 'meeting')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_task_categories_kind",
                table: "task_categories",
                sql: "kind IN ('task', 'bug', 'story', 'feature', 'testing', 'meeting', 'checklist')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_support_tickets_status",
                table: "support_tickets",
                sql: "status IN ('open', 'in_progress', 'resolved', 'closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sprint_activity_logs_from_status",
                table: "sprint_activity_logs",
                sql: "from_status IN ('draft', 'active', 'complete', 'achieved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sprint_activity_logs_to_status",
                table: "sprint_activity_logs",
                sql: "to_status IN ('draft', 'active', 'complete', 'achieved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_seat_increase_requests_status",
                table: "seat_increase_requests",
                sql: "status IN ('pending', 'approved', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_calendar_reminder_type",
                table: "release_calendar",
                sql: "reminder_type IN ('project_release')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_member_invitations_invite_type",
                table: "project_member_invitations",
                sql: "invite_type IN ('member', 'leader')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_member_invitations_status",
                table: "project_member_invitations",
                sql: "status IN ('pending', 'accepted', 'declined', 'expired', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_position_type",
                table: "positions",
                sql: "position_type IN ('unique', 'pooled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_position_assignments_assignment_kind",
                table: "position_assignments",
                sql: "assignment_kind IN ('PrimaryEmployment', 'AdditionalAuthority')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_platform_users_invite_status",
                table: "platform_users",
                sql: "invite_status IN ('pending', 'accepted', 'revoked', 'expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_platform_users_mfa_status",
                table: "platform_users",
                sql: "mfa_status IN ('not_enrolled', 'enrolled', 'locked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_platform_users_status",
                table: "platform_users",
                sql: "status IN ('pending', 'active', 'inactive')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_platform_auth_events_event_type",
                table: "platform_auth_events",
                sql: "event_type IN ('login_succeeded', 'login_failed', 'session_revoked', 'logout_succeeded', 'admin_password_reset_requested', 'admin_password_reset_completed', 'platform_manager_invited', 'platform_manager_invite_accepted', 'mfa_reset')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_personal_calendar_events_event_status",
                table: "personal_calendar_events",
                sql: "event_status IN ('confirmed', 'tentative', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_personal_calendar_events_source_type",
                table: "personal_calendar_events",
                sql: "source_type IN ('manual', 'external_sync', 'holiday')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_permission_changes_kind",
                table: "permission_changes",
                sql: "kind IN ('rename', 'default-grant', 'soft-retire', 'remove')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offboarding_task_bypass_requests_prior_task_status",
                table: "offboarding_task_bypass_requests",
                sql: "prior_task_status IN ('pending', 'in_progress', 'completed', 'bypassed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offboarding_task_bypass_requests_status",
                table: "offboarding_task_bypass_requests",
                sql: "status IN ('pending', 'approved', 'rejected', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offboarding_records_status",
                table: "offboarding_records",
                sql: "status IN ('initiated', 'in_progress', 'completed', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_template_versions_status",
                table: "notification_template_versions",
                sql: "status IN ('draft', 'published')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_deliveries_status",
                table: "notification_deliveries",
                sql: "status IN ('pending', 'provider_accepted', 'available_in_inbox', 'forwarded_to_tray', 'client_accepted', 'skipped', 'expired', 'failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_face_scans_status",
                table: "monitoring_face_scans",
                sql: "status IN ('pending_scan', 'available', 'failed', 'verified', 'not_matched', 'no_reference_photo')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_evidence_assets_evidence_type",
                table: "monitoring_evidence_assets",
                sql: "evidence_type IN ('screenshot')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_evidence_assets_trigger_type",
                table: "monitoring_evidence_assets",
                sql: "trigger_type IN ('on_demand', 'periodic', 'inactivity_approved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_meeting_occurrence_attendees_attendee_kind",
                table: "meeting_occurrence_attendees",
                sql: "attendee_kind IN ('employee', 'guest')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_meeting_details_attendance_status",
                table: "meeting_details",
                sql: "attendance_status IN ('pending', 'provisional', 'synced', 'attendance_unavailable', 'sync_failed', 'not_applicable')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_management_coverage_records_covered_target_type",
                table: "management_coverage_records",
                sql: "covered_target_type IN ('Position', 'Department', 'Company')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_management_coverage_records_status",
                table: "management_coverage_records",
                sql: "status IN ('active', 'inactive')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_location_change_requests_status",
                table: "location_change_requests",
                sql: "status IN ('pending', 'approved', 'rejected', 'cancelled', 'applied')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leave_policy_assignments_target_kind",
                table: "leave_policy_assignments",
                sql: "target_kind IN ('legal_entity', 'department', 'position', 'employee')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_idempotency_records_status",
                table: "idempotency_records",
                sql: "status IN ('in_progress', 'completed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_file_upload_reservations_status",
                table: "file_upload_reservations",
                sql: "status IN ('active', 'completed', 'expired', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_file_records_status",
                table: "file_records",
                sql: "status IN ('pending_scan', 'available', 'quarantined', 'deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_calendar_event_links_sync_status",
                table: "external_calendar_event_links",
                sql: "sync_status IN ('synced', 'pending', 'failed', 'skipped', 'conflict')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_calendar_connections_status",
                table: "external_calendar_connections",
                sql: "status IN ('active', 'reauth_required', 'paused', 'revoked', 'failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_assets_owner_type",
                table: "entity_assets",
                sql: "owner_type IN ('project', 'objective', 'task', 'comment', 'employee', 'legal_entity', 'employee_document', 'checklist_template', 'conversation')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_checklist_tasks_owner_type",
                table: "employee_checklist_tasks",
                sql: "owner_type IN ('employee', 'manager', 'hr', 'it', 'custom_user')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_checklist_tasks_status",
                table: "employee_checklist_tasks",
                sql: "status IN ('pending', 'in_progress', 'completed', 'bypassed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_change_requests_status",
                table: "device_change_requests",
                sql: "status IN ('pending', 'approved', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_messages_kind",
                table: "conversation_messages",
                sql: "kind IN ('text', 'system', 'evidence', 'case')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_message_revisions_kind",
                table: "conversation_message_revisions",
                sql: "kind IN ('edit', 'delete')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clock_in_policies_scope_type",
                table: "clock_in_policies",
                sql: "scope_type IN ('full_company', 'department', 'position', 'employee')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_calendar_events_status",
                table: "calendar_events",
                sql: "status IN ('active', 'archived')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_calendar_event_participants_response_status",
                table: "calendar_event_participants",
                sql: "response_status IN ('pending', 'accepted', 'rejected', 'resolution_requested', 'replacement_nominated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_calendar_event_meetings_owner_kind",
                table: "calendar_event_meetings",
                sql: "owner_kind IN ('calendar_event', 'meeting_series')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_calendar_event_meetings_status",
                table: "calendar_event_meetings",
                sql: "status IN ('active', 'cancelled', 'failed', 'provisioning', 'provisioning_unknown', 'unresolved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attendance_corrections_correction_type",
                table: "attendance_corrections",
                sql: "correction_type IN ('clock_in', 'clock_out', 'break', 'full_day')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attendance_corrections_status",
                table: "attendance_corrections",
                sql: "status IN ('pending', 'approved', 'rejected', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agent_commands_command_type",
                table: "agent_commands",
                sql: "command_type IN ('capture_screenshot')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agent_commands_status",
                table: "agent_commands",
                sql: "status IN ('pending', 'delivered', 'completed', 'failed', 'expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_grant_requests_action_type",
                table: "access_grant_requests",
                sql: "action_type IN ('onboarding_position_access', 'position_change_access')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_grant_requests_approval_status",
                table: "access_grant_requests",
                sql: "approval_status IN ('Pending', 'Approved', 'Rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_area_change_requests_status",
                table: "work_area_change_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wm_notification_log_kind",
                table: "wm_notification_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wm_monitor_alerts_target_type",
                table: "wm_monitor_alerts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wm_approval_requests_action_type",
                table: "wm_approval_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wm_approval_requests_status",
                table: "wm_approval_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wm_approval_requests_target_type",
                table: "wm_approval_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_task_kind",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_task_categories_kind",
                table: "task_categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_support_tickets_status",
                table: "support_tickets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sprint_activity_logs_from_status",
                table: "sprint_activity_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sprint_activity_logs_to_status",
                table: "sprint_activity_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_seat_increase_requests_status",
                table: "seat_increase_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_calendar_reminder_type",
                table: "release_calendar");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_member_invitations_invite_type",
                table: "project_member_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_member_invitations_status",
                table: "project_member_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_position_type",
                table: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_position_assignments_assignment_kind",
                table: "position_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_platform_users_invite_status",
                table: "platform_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_platform_users_mfa_status",
                table: "platform_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_platform_users_status",
                table: "platform_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_platform_auth_events_event_type",
                table: "platform_auth_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_personal_calendar_events_event_status",
                table: "personal_calendar_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_personal_calendar_events_source_type",
                table: "personal_calendar_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_permission_changes_kind",
                table: "permission_changes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offboarding_task_bypass_requests_prior_task_status",
                table: "offboarding_task_bypass_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offboarding_task_bypass_requests_status",
                table: "offboarding_task_bypass_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offboarding_records_status",
                table: "offboarding_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_template_versions_status",
                table: "notification_template_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_deliveries_status",
                table: "notification_deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_face_scans_status",
                table: "monitoring_face_scans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_evidence_assets_evidence_type",
                table: "monitoring_evidence_assets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_evidence_assets_trigger_type",
                table: "monitoring_evidence_assets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_meeting_occurrence_attendees_attendee_kind",
                table: "meeting_occurrence_attendees");

            migrationBuilder.DropCheckConstraint(
                name: "ck_meeting_details_attendance_status",
                table: "meeting_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_management_coverage_records_covered_target_type",
                table: "management_coverage_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_management_coverage_records_status",
                table: "management_coverage_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_location_change_requests_status",
                table: "location_change_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leave_policy_assignments_target_kind",
                table: "leave_policy_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_idempotency_records_status",
                table: "idempotency_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_file_upload_reservations_status",
                table: "file_upload_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_file_records_status",
                table: "file_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_external_calendar_event_links_sync_status",
                table: "external_calendar_event_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_external_calendar_connections_status",
                table: "external_calendar_connections");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_assets_owner_type",
                table: "entity_assets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_checklist_tasks_owner_type",
                table: "employee_checklist_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_checklist_tasks_status",
                table: "employee_checklist_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_change_requests_status",
                table: "device_change_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_messages_kind",
                table: "conversation_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_message_revisions_kind",
                table: "conversation_message_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clock_in_policies_scope_type",
                table: "clock_in_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_calendar_events_status",
                table: "calendar_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_calendar_event_participants_response_status",
                table: "calendar_event_participants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_calendar_event_meetings_owner_kind",
                table: "calendar_event_meetings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_calendar_event_meetings_status",
                table: "calendar_event_meetings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attendance_corrections_correction_type",
                table: "attendance_corrections");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attendance_corrections_status",
                table: "attendance_corrections");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agent_commands_command_type",
                table: "agent_commands");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agent_commands_status",
                table: "agent_commands");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_grant_requests_action_type",
                table: "access_grant_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_grant_requests_approval_status",
                table: "access_grant_requests");
        }
    }
}
