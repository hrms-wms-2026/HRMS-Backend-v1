using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreForeignKeys39b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_position_objective_id",
                table: "wm_approval_requests",
                column: "position_objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_creator_position_objective_id",
                table: "tasks",
                column: "creator_position_objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_edit_request_id",
                table: "task_edit_logs",
                column: "edit_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_drafts_owner_user_id",
                table: "task_drafts",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_drafts_project_id",
                table: "task_drafts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_assigned_by_id",
                table: "task_assignments",
                column: "assigned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_user_id",
                table: "task_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprints_creator_position_objective_id",
                table: "sprints",
                column: "creator_position_objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_recipient_user_id",
                table: "release_calendar",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_invited_by_id",
                table: "project_member_invitations",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_invited_employee_id",
                table: "project_member_invitations",
                column: "invited_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_default_role_id",
                table: "positions",
                column: "default_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_created_by_user_id",
                table: "position_reporting_history",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_avatar_file_id",
                table: "onboarding_drafts",
                column: "avatar_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_selected_template_id",
                table: "onboarding_drafts",
                column: "selected_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_started_by_id",
                table: "onboarding_drafts",
                column: "started_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_approver_id",
                table: "offboarding_task_bypass_requests",
                column: "approver_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_requested_by_id",
                table: "offboarding_task_bypass_requests",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_initiated_by_id",
                table: "offboarding_records",
                column: "initiated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_creator_position_objective_id",
                table: "objectives",
                column: "creator_position_objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_suppressions_created_by_employee_id",
                table: "meeting_occurrence_suppressions",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_action_items_created_by_employee_id",
                table: "meeting_action_items",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_leave_policy_version_id",
                table: "leave_requests",
                column: "leave_policy_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_delegated_from_approver_id",
                table: "leave_request_approvers",
                column: "delegated_from_approver_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_leave_policy_version_id",
                table: "leave_entitlements",
                column: "leave_policy_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_leave_policy_version_id",
                table: "leave_balance_audits",
                column: "leave_policy_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_source_position_assignment_id",
                table: "employee_hierarchy_closure",
                column: "source_position_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_archived_by_id",
                table: "calendar_events",
                column: "archived_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_activity_logs_performed_by_id",
                table: "calendar_event_activity_logs",
                column: "performed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_created_by_user_id",
                table: "bulk_onboarding_batches",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_default_checklist_template_id",
                table: "bulk_onboarding_batches",
                column: "default_checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_default_work_mode_id",
                table: "bulk_onboarding_batches",
                column: "default_work_mode_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_department_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_position_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_reports_to_employee_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_reports_to_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_template_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_work_mode_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_checklist_templates_resolved_template_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_template_id",
                principalTable: "checklist_templates",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_departments_resolved_department_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_department_id",
                principalTable: "departments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_employees_resolved_reports_to_employee_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_reports_to_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_positions_resolved_position_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_position_id",
                principalTable: "positions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_tenant_work_modes_resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows",
                column: "resolved_work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batches_checklist_templates_default_checklist_template_id",
                table: "bulk_onboarding_batches",
                column: "default_checklist_template_id",
                principalTable: "checklist_templates",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batches_tenant_work_modes_default_work_mode_id",
                table: "bulk_onboarding_batches",
                column: "default_work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batches_users_created_by_user_id",
                table: "bulk_onboarding_batches",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_activity_logs_employees_performed_by_id",
                table: "calendar_event_activity_logs",
                column: "performed_by_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_events_employees_archived_by_id",
                table: "calendar_events",
                column: "archived_by_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_hierarchy_closure_position_assignments_source_position_assignment_id",
                table: "employee_hierarchy_closure",
                column: "source_position_assignment_id",
                principalTable: "position_assignments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_users_user_id",
                table: "employees",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_balance_audits_leave_policy_versions_leave_policy_version_id",
                table: "leave_balance_audits",
                column: "leave_policy_version_id",
                principalTable: "leave_policy_versions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_entitlements_leave_policy_versions_leave_policy_version_id",
                table: "leave_entitlements",
                column: "leave_policy_version_id",
                principalTable: "leave_policy_versions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_approvers_employees_delegated_from_approver_id",
                table: "leave_request_approvers",
                column: "delegated_from_approver_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_requests_leave_policy_versions_leave_policy_version_id",
                table: "leave_requests",
                column: "leave_policy_version_id",
                principalTable: "leave_policy_versions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_action_items_employees_created_by_employee_id",
                table: "meeting_action_items",
                column: "created_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_occurrence_suppressions_employees_created_by_employee_id",
                table: "meeting_occurrence_suppressions",
                column: "created_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_series_wm_approval_requests_approval_request_id",
                table: "meeting_series",
                column: "approval_request_id",
                principalTable: "wm_approval_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_objectives_objectives_creator_position_objective_id",
                table: "objectives",
                column: "creator_position_objective_id",
                principalTable: "objectives",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_offboarding_records_users_initiated_by_id",
                table: "offboarding_records",
                column: "initiated_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_offboarding_task_bypass_requests_users_approver_id",
                table: "offboarding_task_bypass_requests",
                column: "approver_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_offboarding_task_bypass_requests_users_requested_by_id",
                table: "offboarding_task_bypass_requests",
                column: "requested_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_checklist_templates_selected_template_id",
                table: "onboarding_drafts",
                column: "selected_template_id",
                principalTable: "checklist_templates",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_file_records_avatar_file_id",
                table: "onboarding_drafts",
                column: "avatar_file_id",
                principalTable: "file_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_users_started_by_id",
                table: "onboarding_drafts",
                column: "started_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_position_reporting_history_users_created_by_user_id",
                table: "position_reporting_history",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_positions_roles_default_role_id",
                table: "positions",
                column: "default_role_id",
                principalTable: "roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_member_invitations_employees_invited_by_id",
                table: "project_member_invitations",
                column: "invited_by_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_member_invitations_employees_invited_employee_id",
                table: "project_member_invitations",
                column: "invited_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_release_calendar_users_recipient_user_id",
                table: "release_calendar",
                column: "recipient_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sprints_objectives_creator_position_objective_id",
                table: "sprints",
                column: "creator_position_objective_id",
                principalTable: "objectives",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignments_employees_assigned_by_id",
                table: "task_assignments",
                column: "assigned_by_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_assignments_users_user_id",
                table: "task_assignments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_drafts_projects_project_id",
                table: "task_drafts",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_drafts_users_owner_user_id",
                table: "task_drafts",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_edit_logs_wm_approval_requests_edit_request_id",
                table: "task_edit_logs",
                column: "edit_request_id",
                principalTable: "wm_approval_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_objectives_creator_position_objective_id",
                table: "tasks",
                column: "creator_position_objective_id",
                principalTable: "objectives",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_objectives_position_objective_id",
                table: "wm_approval_requests",
                column: "position_objective_id",
                principalTable: "objectives",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_checklist_templates_resolved_template_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_departments_resolved_department_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_employees_resolved_reports_to_employee_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_positions_resolved_position_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_tenant_work_modes_resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batches_checklist_templates_default_checklist_template_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batches_tenant_work_modes_default_work_mode_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batches_users_created_by_user_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_activity_logs_employees_performed_by_id",
                table: "calendar_event_activity_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_events_employees_archived_by_id",
                table: "calendar_events");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_hierarchy_closure_position_assignments_source_position_assignment_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_users_user_id",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_balance_audits_leave_policy_versions_leave_policy_version_id",
                table: "leave_balance_audits");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_entitlements_leave_policy_versions_leave_policy_version_id",
                table: "leave_entitlements");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_approvers_employees_delegated_from_approver_id",
                table: "leave_request_approvers");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_requests_leave_policy_versions_leave_policy_version_id",
                table: "leave_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_action_items_employees_created_by_employee_id",
                table: "meeting_action_items");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_occurrence_suppressions_employees_created_by_employee_id",
                table: "meeting_occurrence_suppressions");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_series_wm_approval_requests_approval_request_id",
                table: "meeting_series");

            migrationBuilder.DropForeignKey(
                name: "fk_objectives_objectives_creator_position_objective_id",
                table: "objectives");

            migrationBuilder.DropForeignKey(
                name: "fk_offboarding_records_users_initiated_by_id",
                table: "offboarding_records");

            migrationBuilder.DropForeignKey(
                name: "fk_offboarding_task_bypass_requests_users_approver_id",
                table: "offboarding_task_bypass_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_offboarding_task_bypass_requests_users_requested_by_id",
                table: "offboarding_task_bypass_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_checklist_templates_selected_template_id",
                table: "onboarding_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_file_records_avatar_file_id",
                table: "onboarding_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_users_started_by_id",
                table: "onboarding_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_position_reporting_history_users_created_by_user_id",
                table: "position_reporting_history");

            migrationBuilder.DropForeignKey(
                name: "fk_positions_roles_default_role_id",
                table: "positions");

            migrationBuilder.DropForeignKey(
                name: "fk_project_member_invitations_employees_invited_by_id",
                table: "project_member_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_project_member_invitations_employees_invited_employee_id",
                table: "project_member_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_release_calendar_users_recipient_user_id",
                table: "release_calendar");

            migrationBuilder.DropForeignKey(
                name: "fk_sprints_objectives_creator_position_objective_id",
                table: "sprints");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignments_employees_assigned_by_id",
                table: "task_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_task_assignments_users_user_id",
                table: "task_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_task_drafts_projects_project_id",
                table: "task_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_task_drafts_users_owner_user_id",
                table: "task_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_task_edit_logs_wm_approval_requests_edit_request_id",
                table: "task_edit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_objectives_creator_position_objective_id",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_objectives_position_objective_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_position_objective_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_tasks_creator_position_objective_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_edit_logs_edit_request_id",
                table: "task_edit_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_drafts_owner_user_id",
                table: "task_drafts");

            migrationBuilder.DropIndex(
                name: "ix_task_drafts_project_id",
                table: "task_drafts");

            migrationBuilder.DropIndex(
                name: "ix_task_assignments_assigned_by_id",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "ix_task_assignments_user_id",
                table: "task_assignments");

            migrationBuilder.DropIndex(
                name: "ix_sprints_creator_position_objective_id",
                table: "sprints");

            migrationBuilder.DropIndex(
                name: "ix_release_calendar_recipient_user_id",
                table: "release_calendar");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_invited_by_id",
                table: "project_member_invitations");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_invited_employee_id",
                table: "project_member_invitations");

            migrationBuilder.DropIndex(
                name: "ix_positions_default_role_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_position_reporting_history_created_by_user_id",
                table: "position_reporting_history");

            migrationBuilder.DropIndex(
                name: "ix_onboarding_drafts_avatar_file_id",
                table: "onboarding_drafts");

            migrationBuilder.DropIndex(
                name: "ix_onboarding_drafts_selected_template_id",
                table: "onboarding_drafts");

            migrationBuilder.DropIndex(
                name: "ix_onboarding_drafts_started_by_id",
                table: "onboarding_drafts");

            migrationBuilder.DropIndex(
                name: "ix_offboarding_task_bypass_requests_approver_id",
                table: "offboarding_task_bypass_requests");

            migrationBuilder.DropIndex(
                name: "ix_offboarding_task_bypass_requests_requested_by_id",
                table: "offboarding_task_bypass_requests");

            migrationBuilder.DropIndex(
                name: "ix_offboarding_records_initiated_by_id",
                table: "offboarding_records");

            migrationBuilder.DropIndex(
                name: "ix_objectives_creator_position_objective_id",
                table: "objectives");

            migrationBuilder.DropIndex(
                name: "ix_meeting_occurrence_suppressions_created_by_employee_id",
                table: "meeting_occurrence_suppressions");

            migrationBuilder.DropIndex(
                name: "ix_meeting_action_items_created_by_employee_id",
                table: "meeting_action_items");

            migrationBuilder.DropIndex(
                name: "ix_leave_requests_leave_policy_version_id",
                table: "leave_requests");

            migrationBuilder.DropIndex(
                name: "ix_leave_request_approvers_delegated_from_approver_id",
                table: "leave_request_approvers");

            migrationBuilder.DropIndex(
                name: "ix_leave_entitlements_leave_policy_version_id",
                table: "leave_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_leave_balance_audits_leave_policy_version_id",
                table: "leave_balance_audits");

            migrationBuilder.DropIndex(
                name: "ix_employee_hierarchy_closure_source_position_assignment_id",
                table: "employee_hierarchy_closure");

            migrationBuilder.DropIndex(
                name: "ix_calendar_events_archived_by_id",
                table: "calendar_events");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_activity_logs_performed_by_id",
                table: "calendar_event_activity_logs");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batches_created_by_user_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batches_default_checklist_template_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batches_default_work_mode_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_department_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_position_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_reports_to_employee_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_template_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batch_rows_resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows");
        }
    }
}
