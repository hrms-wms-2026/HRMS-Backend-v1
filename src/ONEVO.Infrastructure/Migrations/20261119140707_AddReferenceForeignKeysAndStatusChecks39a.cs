using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceForeignKeysAndStatusChecks39a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_daily_work_location_confirmations_employees_employee_id",
                table: "daily_work_location_confirmations");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_addresses_employees_employee_id",
                table: "employee_addresses");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_bank_details_employees_employee_id",
                table: "employee_bank_details");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_dependents_employees_employee_id",
                table: "employee_dependents");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_emergency_contacts_employees_employee_id",
                table: "employee_emergency_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_work_locations_employees_employee_id",
                table: "employee_work_locations");

            migrationBuilder.DropForeignKey(
                name: "fk_planned_task_blocks_employees_employee_id",
                table: "planned_task_blocks");

            migrationBuilder.CreateIndex(
                name: "ix_wm_task_rate_settings_legal_entity_id",
                table: "wm_task_rate_settings",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_task_rate_settings_updated_by_employee_id",
                table: "wm_task_rate_settings",
                column: "updated_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_actor_employee_id",
                table: "wm_notification_log",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_approval_request_id",
                table: "wm_notification_log",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_project_id",
                table: "wm_notification_log",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_recipient_employee_id",
                table: "wm_notification_log",
                column: "recipient_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_monitor_alerts_project_id",
                table: "wm_monitor_alerts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_monitor_alerts_subject_employee_id",
                table: "wm_monitor_alerts",
                column: "subject_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_module_hour_summaries_project_id",
                table: "wm_module_hour_summaries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_approver_employee_id",
                table: "wm_approval_requests",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_decided_by_employee_id",
                table: "wm_approval_requests",
                column: "decided_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_project_id",
                table: "wm_approval_requests",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_requested_by_employee_id",
                table: "wm_approval_requests",
                column: "requested_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_reverted_by_employee_id",
                table: "wm_approval_requests",
                column: "reverted_by_employee_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_status",
                table: "tenants",
                sql: "status IN ('Provisioning', 'Trial', 'Active', 'Suspended', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_status_histories_from_status",
                table: "tenant_status_histories",
                sql: "from_status IN ('Provisioning', 'Trial', 'Active', 'Suspended', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_status_histories_to_status",
                table: "tenant_status_histories",
                sql: "to_status IN ('Provisioning', 'Trial', 'Active', 'Suspended', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_objective_id",
                table: "tasks",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id",
                table: "tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_statuses_objective_id",
                table: "task_statuses",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_statuses_project_id",
                table: "task_statuses",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_employee_id",
                table: "task_status_change_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_employee_id",
                table: "task_percentage_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_employee_id",
                table: "task_edit_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_drafts_project_id",
                table: "task_drafts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_employee_id",
                table: "task_comments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_reactions_employee_id",
                table: "task_comment_reactions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_logs_employee_id",
                table: "task_comment_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_employee_id",
                table: "task_clocking_sessions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_categories_project_id",
                table: "task_categories",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_employee_id",
                table: "task_assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_user_id",
                table: "task_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprints_project_id",
                table: "sprints",
                column: "project_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sprints_status",
                table: "sprints",
                sql: "status IN ('draft', 'active', 'complete', 'achieved')");

            migrationBuilder.CreateIndex(
                name: "ix_sprint_activity_logs_employee_id",
                table: "sprint_activity_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_legal_entity_id",
                table: "reporting_manager_changes",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_category_id",
                table: "projects",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_lead_id",
                table: "projects",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_employee_id",
                table: "project_members",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_sessions_employee_id",
                table: "presence_sessions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_reports_to_employee_id",
                table: "position_assignments",
                column: "reports_to_employee_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_position_assignments_assignment_status",
                table: "position_assignments",
                sql: "assignment_status IN ('active', 'planned', 'ended', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_status",
                table: "outbox_messages",
                sql: "status IN ('pending', 'processing', 'published', 'failed')");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_reports_to_employee_id",
                table: "onboarding_drafts",
                column: "reports_to_employee_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_onboarding_drafts_status",
                table: "onboarding_drafts",
                sql: "status IN ('draft', 'waiting_for_seat', 'waiting_for_position_approval', 'cancelled', 'finalized')");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_owner_id",
                table: "objectives",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_reporting_manager_id",
                table: "objectives",
                column: "reporting_manager_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_monitoring_notifications_type",
                table: "monitoring_notifications",
                sql: "type IN ('BreakReminder', 'LongIdleAlert', 'LowActivityAlert', 'FocusNudge', 'OutsideWorkLocationAlert', 'BreakAllowanceExceeded', 'IdentityVerificationAlert')");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_project_id",
                table: "meeting_series",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_responsible_employee_id",
                table: "management_coverage_records",
                column: "responsible_employee_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leave_requests_status",
                table: "leave_requests",
                sql: "status IN ('pending', 'approved', 'rejected', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leave_request_day_allocations_status",
                table: "leave_request_day_allocations",
                sql: "status IN ('active', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leave_request_approvers_status",
                table: "leave_request_approvers",
                sql: "status IN ('pending', 'approved', 'rejected', 'skipped', 'cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_leave_policy_id",
                table: "leave_entitlements",
                column: "leave_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_leave_policy_id",
                table: "leave_balance_audits",
                column: "leave_policy_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leave_balance_audits_change_type",
                table: "leave_balance_audits",
                sql: "change_type IN ('accrual', 'deduction', 'carry_forward', 'forfeiture', 'adjustment')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exceptions_status",
                table: "exceptions",
                sql: "status IN ('Open', 'Acknowledged', 'Resolved', 'Escalated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exceptions_type",
                table: "exceptions",
                sql: "type IN ('SustainedLowActivity', 'AttendanceIrregularity', 'UnusualActivityPattern', 'IdentityAnomaly', 'MeetingNoShow', 'MeetingLateJoin', 'MeetingEarlyLeave', 'MeetingRsvp', 'MeetingConflict', 'MeetingOverload')");

            migrationBuilder.CreateIndex(
                name: "ix_employees_department_id",
                table: "employees",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_legal_entity_id",
                table: "employees",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_ancestor_employee_id",
                table: "employee_hierarchy_closure",
                column: "ancestor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_descendant_employee_id",
                table: "employee_hierarchy_closure",
                column: "descendant_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_source_position_assignment_id",
                table: "employee_hierarchy_closure",
                column: "source_position_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_time_off_type_id",
                table: "clock_in_late_deduction_rules",
                column: "time_off_type_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bulk_onboarding_batches_status",
                table: "bulk_onboarding_batches",
                sql: "status IN ('mapping_pending', 'validated', 'draft_creation_pending', 'drafts_created', 'finalize_pending', 'finalize_completed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bulk_onboarding_batch_rows_status",
                table: "bulk_onboarding_batch_rows",
                sql: "status IN ('pending_mapping', 'valid', 'invalid', 'draft_created', 'draft_failed', 'finalized', 'waiting_for_seat', 'waiting_for_position_approval', 'finalize_failed')");

            migrationBuilder.CreateIndex(
                name: "ix_break_records_employee_id",
                table: "break_records",
                column: "employee_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_biometric_profiles_status",
                table: "biometric_profiles",
                sql: "status IN ('Enrolled', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_biometric_enrollment_attempts_status",
                table: "biometric_enrollment_attempts",
                sql: "status IN ('Pending', 'Succeeded', 'Failed', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_employee_id",
                table: "attendance_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_expected_work_mode_id",
                table: "attendance_records",
                column: "expected_work_mode_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attendance_records_status",
                table: "attendance_records",
                sql: "status IN ('on_time', 'late', 'active', 'clocked_out', 'off_day', 'no_schedule', 'policy_not_configured', 'not_clocked_in', 'short_hours', 'normal', 'on_time_off', 'worked_during_time_off', 'non_working_day', 'worked_on_non_working_day', 'over_break', 'on_break', 'missing_clock_out')");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alerts_converted_leave_request_id",
                table: "attendance_alerts",
                column: "converted_leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alerts_employee_id",
                table: "attendance_alerts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alerts_legal_entity_id",
                table: "attendance_alerts",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alerts_resolved_by_employee_id",
                table: "attendance_alerts",
                column: "resolved_by_employee_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attendance_alerts_status",
                table: "attendance_alerts",
                sql: "status IN ('open', 'resolved')");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alert_deductions_charge_audit_id",
                table: "attendance_alert_deductions",
                column: "charge_audit_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alert_deductions_employee_id",
                table: "attendance_alert_deductions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alert_deductions_leave_type_id",
                table: "attendance_alert_deductions",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alert_deductions_refund_audit_id",
                table: "attendance_alert_deductions",
                column: "refund_audit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attendance_alert_deductions_status",
                table: "attendance_alert_deductions",
                sql: "status IN ('pending', 'charged', 'refunded')");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_reserved_position_assignment_id",
                table: "access_grant_requests",
                column: "reserved_position_assignment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_access_grant_requests_position_assignments_reserved_position_assignment_id",
                table: "access_grant_requests",
                column: "reserved_position_assignment_id",
                principalTable: "position_assignments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alert_deductions_employees_employee_id",
                table: "attendance_alert_deductions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alert_deductions_leave_balance_audits_charge_audit_id",
                table: "attendance_alert_deductions",
                column: "charge_audit_id",
                principalTable: "leave_balance_audits",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alert_deductions_leave_balance_audits_refund_audit_id",
                table: "attendance_alert_deductions",
                column: "refund_audit_id",
                principalTable: "leave_balance_audits",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alert_deductions_leave_types_leave_type_id",
                table: "attendance_alert_deductions",
                column: "leave_type_id",
                principalTable: "leave_types",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alerts_employees_employee_id",
                table: "attendance_alerts",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alerts_employees_resolved_by_employee_id",
                table: "attendance_alerts",
                column: "resolved_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alerts_leave_requests_converted_leave_request_id",
                table: "attendance_alerts",
                column: "converted_leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alerts_legal_entities_legal_entity_id",
                table: "attendance_alerts",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_employees_employee_id",
                table: "attendance_records",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_tenant_work_modes_expected_work_mode_id",
                table: "attendance_records",
                column: "expected_work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_break_records_employees_employee_id",
                table: "break_records",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_clock_in_late_deduction_rules_leave_types_time_off_type_id",
                table: "clock_in_late_deduction_rules",
                column: "time_off_type_id",
                principalTable: "leave_types",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_daily_work_location_confirmations_employees_employee_id",
                table: "daily_work_location_confirmations",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_addresses_employees_employee_id",
                table: "employee_addresses",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_bank_details_employees_employee_id",
                table: "employee_bank_details",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_dependents_employees_employee_id",
                table: "employee_dependents",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_emergency_contacts_employees_employee_id",
                table: "employee_emergency_contacts",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_hierarchy_closure_employees_ancestor_employee_id",
                table: "employee_hierarchy_closure",
                column: "ancestor_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_hierarchy_closure_employees_descendant_employee_id",
                table: "employee_hierarchy_closure",
                column: "descendant_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_hierarchy_closure_position_assignments_source_position_assignment_id",
                table: "employee_hierarchy_closure",
                column: "source_position_assignment_id",
                principalTable: "position_assignments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_work_locations_employees_employee_id",
                table: "employee_work_locations",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_departments_department_id",
                table: "employees",
                column: "department_id",
                principalTable: "departments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_legal_entities_legal_entity_id",
                table: "employees",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_users_user_id",
                table: "employees",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_balance_audits_leave_policies_leave_policy_id",
                table: "leave_balance_audits",
                column: "leave_policy_id",
                principalTable: "leave_policies",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_entitlements_leave_policies_leave_policy_id",
                table: "leave_entitlements",
                column: "leave_policy_id",
                principalTable: "leave_policies",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_management_coverage_records_employees_responsible_employee_id",
                table: "management_coverage_records",
                column: "responsible_employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_series_projects_project_id",
                table: "meeting_series",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_objectives_employees_owner_id",
                table: "objectives",
                column: "owner_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_objectives_employees_reporting_manager_id",
                table: "objectives",
                column: "reporting_manager_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_employees_reports_to_employee_id",
                table: "onboarding_drafts",
                column: "reports_to_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_planned_task_blocks_employees_employee_id",
                table: "planned_task_blocks",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_position_assignments_employees_reports_to_employee_id",
                table: "position_assignments",
                column: "reports_to_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_presence_sessions_employees_employee_id",
                table: "presence_sessions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_members_employees_employee_id",
                table: "project_members",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_employees_lead_id",
                table: "projects",
                column: "lead_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_project_categories_category_id",
                table: "projects",
                column: "category_id",
                principalTable: "project_categories",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_reporting_manager_changes_legal_entities_legal_entity_id",
                table: "reporting_manager_changes",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sprint_activity_logs_employees_employee_id",
                table: "sprint_activity_logs",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sprints_projects_project_id",
                table: "sprints",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignments_employees_employee_id",
                table: "task_assignments",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignments_users_user_id",
                table: "task_assignments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_categories_projects_project_id",
                table: "task_categories",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_clocking_sessions_employees_employee_id",
                table: "task_clocking_sessions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_comment_logs_employees_employee_id",
                table: "task_comment_logs",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_comment_reactions_employees_employee_id",
                table: "task_comment_reactions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_comments_employees_employee_id",
                table: "task_comments",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_drafts_projects_project_id",
                table: "task_drafts",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_edit_logs_employees_employee_id",
                table: "task_edit_logs",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_percentage_logs_employees_employee_id",
                table: "task_percentage_logs",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_status_change_logs_employees_employee_id",
                table: "task_status_change_logs",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_statuses_objectives_objective_id",
                table: "task_statuses",
                column: "objective_id",
                principalTable: "objectives",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_statuses_projects_project_id",
                table: "task_statuses",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_objectives_objective_id",
                table: "tasks",
                column: "objective_id",
                principalTable: "objectives",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_projects_project_id",
                table: "tasks",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_employees_approver_employee_id",
                table: "wm_approval_requests",
                column: "approver_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_employees_decided_by_employee_id",
                table: "wm_approval_requests",
                column: "decided_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_employees_requested_by_employee_id",
                table: "wm_approval_requests",
                column: "requested_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_employees_reverted_by_employee_id",
                table: "wm_approval_requests",
                column: "reverted_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_projects_project_id",
                table: "wm_approval_requests",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_module_hour_summaries_projects_project_id",
                table: "wm_module_hour_summaries",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_monitor_alerts_employees_subject_employee_id",
                table: "wm_monitor_alerts",
                column: "subject_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_monitor_alerts_projects_project_id",
                table: "wm_monitor_alerts",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_notification_log_employees_actor_employee_id",
                table: "wm_notification_log",
                column: "actor_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_notification_log_employees_recipient_employee_id",
                table: "wm_notification_log",
                column: "recipient_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_notification_log_projects_project_id",
                table: "wm_notification_log",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_notification_log_wm_approval_requests_approval_request_id",
                table: "wm_notification_log",
                column: "approval_request_id",
                principalTable: "wm_approval_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_task_rate_settings_employees_updated_by_employee_id",
                table: "wm_task_rate_settings",
                column: "updated_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_task_rate_settings_legal_entities_legal_entity_id",
                table: "wm_task_rate_settings",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_access_grant_requests_position_assignments_reserved_position_assignment_id",
                table: "access_grant_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alert_deductions_employees_employee_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alert_deductions_leave_balance_audits_charge_audit_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alert_deductions_leave_balance_audits_refund_audit_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alert_deductions_leave_types_leave_type_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alerts_employees_employee_id",
                table: "attendance_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alerts_employees_resolved_by_employee_id",
                table: "attendance_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alerts_leave_requests_converted_leave_request_id",
                table: "attendance_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alerts_legal_entities_legal_entity_id",
                table: "attendance_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_employees_employee_id",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_tenant_work_modes_expected_work_mode_id",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_break_records_employees_employee_id",
                table: "break_records");

            migrationBuilder.DropForeignKey(
                name: "fk_clock_in_late_deduction_rules_leave_types_time_off_type_id",
                table: "clock_in_late_deduction_rules");

            migrationBuilder.DropForeignKey(
                name: "fk_daily_work_location_confirmations_employees_employee_id",
                table: "daily_work_location_confirmations");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_addresses_employees_employee_id",
                table: "employee_addresses");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_bank_details_employees_employee_id",
                table: "employee_bank_details");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_dependents_employees_employee_id",
                table: "employee_dependents");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_emergency_contacts_employees_employee_id",
                table: "employee_emergency_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_hierarchy_closure_employees_ancestor_employee_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_hierarchy_closure_employees_descendant_employee_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_hierarchy_closure_position_assignments_source_position_assignment_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_work_locations_employees_employee_id",
                table: "employee_work_locations");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_departments_department_id",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_legal_entities_legal_entity_id",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_users_user_id",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_balance_audits_leave_policies_leave_policy_id",
                table: "leave_balance_audits");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_entitlements_leave_policies_leave_policy_id",
                table: "leave_entitlements");

            migrationBuilder.DropForeignKey(
                name: "fk_management_coverage_records_employees_responsible_employee_id",
                table: "management_coverage_records");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_series_projects_project_id",
                table: "meeting_series");

            migrationBuilder.DropForeignKey(
                name: "fk_objectives_employees_owner_id",
                table: "objectives");

            migrationBuilder.DropForeignKey(
                name: "fk_objectives_employees_reporting_manager_id",
                table: "objectives");

            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_employees_reports_to_employee_id",
                table: "onboarding_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_planned_task_blocks_employees_employee_id",
                table: "planned_task_blocks");

            migrationBuilder.DropForeignKey(
                name: "fk_position_assignments_employees_reports_to_employee_id",
                table: "position_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_presence_sessions_employees_employee_id",
                table: "presence_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_project_members_employees_employee_id",
                table: "project_members");

            migrationBuilder.DropForeignKey(
                name: "fk_projects_employees_lead_id",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_projects_project_categories_category_id",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_reporting_manager_changes_legal_entities_legal_entity_id",
                table: "reporting_manager_changes");

            migrationBuilder.DropForeignKey(
                name: "fk_sprint_activity_logs_employees_employee_id",
                table: "sprint_activity_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_sprints_projects_project_id",
                table: "sprints");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignments_employees_employee_id",
                table: "task_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignments_users_user_id",
                table: "task_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_task_categories_projects_project_id",
                table: "task_categories");

            migrationBuilder.DropForeignKey(
                name: "fk_task_clocking_sessions_employees_employee_id",
                table: "task_clocking_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comment_logs_employees_employee_id",
                table: "task_comment_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comment_reactions_employees_employee_id",
                table: "task_comment_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comments_employees_employee_id",
                table: "task_comments");

            migrationBuilder.DropForeignKey(
                name: "fk_task_drafts_projects_project_id",
                table: "task_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_task_edit_logs_employees_employee_id",
                table: "task_edit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_percentage_logs_employees_employee_id",
                table: "task_percentage_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_status_change_logs_employees_employee_id",
                table: "task_status_change_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_statuses_objectives_objective_id",
                table: "task_statuses");

            migrationBuilder.DropForeignKey(
                name: "fk_task_statuses_projects_project_id",
                table: "task_statuses");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_objectives_objective_id",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_projects_project_id",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_employees_approver_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_employees_decided_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_employees_requested_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_employees_reverted_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_projects_project_id",
                table: "wm_approval_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_module_hour_summaries_projects_project_id",
                table: "wm_module_hour_summaries");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_monitor_alerts_employees_subject_employee_id",
                table: "wm_monitor_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_monitor_alerts_projects_project_id",
                table: "wm_monitor_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_notification_log_employees_actor_employee_id",
                table: "wm_notification_log");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_notification_log_employees_recipient_employee_id",
                table: "wm_notification_log");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_notification_log_projects_project_id",
                table: "wm_notification_log");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_notification_log_wm_approval_requests_approval_request_id",
                table: "wm_notification_log");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_task_rate_settings_employees_updated_by_employee_id",
                table: "wm_task_rate_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_task_rate_settings_legal_entities_legal_entity_id",
                table: "wm_task_rate_settings");

            migrationBuilder.DropIndex(
                name: "ix_wm_task_rate_settings_legal_entity_id",
                table: "wm_task_rate_settings");

            migrationBuilder.DropIndex(
                name: "ix_wm_task_rate_settings_updated_by_employee_id",
                table: "wm_task_rate_settings");

            migrationBuilder.DropIndex(
                name: "ix_wm_notification_log_actor_employee_id",
                table: "wm_notification_log");

            migrationBuilder.DropIndex(
                name: "ix_wm_notification_log_approval_request_id",
                table: "wm_notification_log");

            migrationBuilder.DropIndex(
                name: "ix_wm_notification_log_project_id",
                table: "wm_notification_log");

            migrationBuilder.DropIndex(
                name: "ix_wm_notification_log_recipient_employee_id",
                table: "wm_notification_log");

            migrationBuilder.DropIndex(
                name: "ix_wm_monitor_alerts_project_id",
                table: "wm_monitor_alerts");

            migrationBuilder.DropIndex(
                name: "ix_wm_monitor_alerts_subject_employee_id",
                table: "wm_monitor_alerts");

            migrationBuilder.DropIndex(
                name: "ix_wm_module_hour_summaries_project_id",
                table: "wm_module_hour_summaries");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_approver_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_decided_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_project_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_requested_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_reverted_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_status",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_status_histories_from_status",
                table: "tenant_status_histories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_status_histories_to_status",
                table: "tenant_status_histories");

            migrationBuilder.DropIndex(
                name: "ix_tasks_objective_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_project_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_statuses_objective_id",
                table: "task_statuses");

            migrationBuilder.DropIndex(
                name: "ix_task_statuses_project_id",
                table: "task_statuses");

            migrationBuilder.DropIndex(
                name: "ix_task_status_change_logs_employee_id",
                table: "task_status_change_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_percentage_logs_employee_id",
                table: "task_percentage_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_edit_logs_employee_id",
                table: "task_edit_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_drafts_project_id",
                table: "task_drafts");

            migrationBuilder.DropIndex(
                name: "ix_task_comments_employee_id",
                table: "task_comments");

            migrationBuilder.DropIndex(
                name: "ix_task_comment_reactions_employee_id",
                table: "task_comment_reactions");

            migrationBuilder.DropIndex(
                name: "ix_task_comment_logs_employee_id",
                table: "task_comment_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_clocking_sessions_employee_id",
                table: "task_clocking_sessions");

            migrationBuilder.DropIndex(
                name: "ix_task_categories_project_id",
                table: "task_categories");

            migrationBuilder.DropIndex(
                name: "ix_task_assignments_employee_id",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "ix_task_assignments_user_id",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "ix_sprints_project_id",
                table: "sprints");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sprints_status",
                table: "sprints");

            migrationBuilder.DropIndex(
                name: "ix_sprint_activity_logs_employee_id",
                table: "sprint_activity_logs");

            migrationBuilder.DropIndex(
                name: "ix_reporting_manager_changes_legal_entity_id",
                table: "reporting_manager_changes");

            migrationBuilder.DropIndex(
                name: "ix_projects_category_id",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_projects_lead_id",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_project_members_employee_id",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "ix_presence_sessions_employee_id",
                table: "presence_sessions");

            migrationBuilder.DropIndex(
                name: "ix_position_assignments_reports_to_employee_id",
                table: "position_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_position_assignments_assignment_status",
                table: "position_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_status",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_onboarding_drafts_reports_to_employee_id",
                table: "onboarding_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_onboarding_drafts_status",
                table: "onboarding_drafts");

            migrationBuilder.DropIndex(
                name: "ix_objectives_owner_id",
                table: "objectives");

            migrationBuilder.DropIndex(
                name: "ix_objectives_reporting_manager_id",
                table: "objectives");

            migrationBuilder.DropCheckConstraint(
                name: "ck_monitoring_notifications_type",
                table: "monitoring_notifications");

            migrationBuilder.DropIndex(
                name: "ix_meeting_series_project_id",
                table: "meeting_series");

            migrationBuilder.DropIndex(
                name: "ix_management_coverage_records_responsible_employee_id",
                table: "management_coverage_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leave_requests_status",
                table: "leave_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leave_request_day_allocations_status",
                table: "leave_request_day_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leave_request_approvers_status",
                table: "leave_request_approvers");

            migrationBuilder.DropIndex(
                name: "ix_leave_entitlements_leave_policy_id",
                table: "leave_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_leave_balance_audits_leave_policy_id",
                table: "leave_balance_audits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leave_balance_audits_change_type",
                table: "leave_balance_audits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_exceptions_status",
                table: "exceptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_exceptions_type",
                table: "exceptions");

            migrationBuilder.DropIndex(
                name: "ix_employees_department_id",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_legal_entity_id",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employee_hierarchy_closure_ancestor_employee_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropIndex(
                name: "ix_employee_hierarchy_closure_descendant_employee_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropIndex(
                name: "ix_employee_hierarchy_closure_source_position_assignment_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropIndex(
                name: "ix_clock_in_late_deduction_rules_time_off_type_id",
                table: "clock_in_late_deduction_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bulk_onboarding_batches_status",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bulk_onboarding_batch_rows_status",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropIndex(
                name: "ix_break_records_employee_id",
                table: "break_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_biometric_profiles_status",
                table: "biometric_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_biometric_enrollment_attempts_status",
                table: "biometric_enrollment_attempts");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_employee_id",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_expected_work_mode_id",
                table: "attendance_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attendance_records_status",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alerts_converted_leave_request_id",
                table: "attendance_alerts");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alerts_employee_id",
                table: "attendance_alerts");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alerts_legal_entity_id",
                table: "attendance_alerts");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alerts_resolved_by_employee_id",
                table: "attendance_alerts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attendance_alerts_status",
                table: "attendance_alerts");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alert_deductions_charge_audit_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alert_deductions_employee_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alert_deductions_leave_type_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alert_deductions_refund_audit_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attendance_alert_deductions_status",
                table: "attendance_alert_deductions");

            migrationBuilder.DropIndex(
                name: "ix_access_grant_requests_reserved_position_assignment_id",
                table: "access_grant_requests");

            migrationBuilder.AddForeignKey(
                name: "fk_daily_work_location_confirmations_employees_employee_id",
                table: "daily_work_location_confirmations",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_employee_addresses_employees_employee_id",
                table: "employee_addresses",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_employee_bank_details_employees_employee_id",
                table: "employee_bank_details",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_employee_dependents_employees_employee_id",
                table: "employee_dependents",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_employee_emergency_contacts_employees_employee_id",
                table: "employee_emergency_contacts",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_employee_work_locations_employees_employee_id",
                table: "employee_work_locations",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_planned_task_blocks_employees_employee_id",
                table: "planned_task_blocks",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
