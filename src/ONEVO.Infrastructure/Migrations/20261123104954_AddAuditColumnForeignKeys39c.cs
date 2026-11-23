using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditColumnForeignKeys39c : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_task_rate_settings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_notification_log",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_monitor_alerts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_module_hour_summaries",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_approval_requests",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "versions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "users",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "user_dashboards",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "tenant_one_time_charges",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_statuses",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_status_change_logs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_percentage_logs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_edit_logs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_drafts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_comments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_comment_reactions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_comment_logs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_clocking_sessions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_categories",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "sprints",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "sprint_activity_logs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "roles",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "reporting_manager_changes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "release_calendar",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "projects",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "project_members",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "project_member_invitations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "project_categories",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "positions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "position_assignments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "planned_task_blocks",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "personal_calendar_events",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "onboarding_drafts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "objectives",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "labels",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "inbox_decision_journal",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "holiday_calendar_settings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "entity_assets",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employees",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_emergency_contacts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_dependents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_bank_details",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_addresses",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_widgets",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_widget_layouts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_templates",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_template_widgets",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_template_widget_layouts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_participants",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_mutes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_messages",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_revisions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_reactions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_mentions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_hides",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_attachments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_favorites",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_meeting_incidents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_event_reminders_sent",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_event_participants",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_event_participant_rsvp_history",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "bulk_onboarding_batches",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "bulk_onboarding_batch_rows",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "attendance_alerts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "attendance_alert_deductions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "ix_wm_task_rate_settings_created_by_id",
                table: "wm_task_rate_settings",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_created_by_id",
                table: "wm_notification_log",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_monitor_alerts_created_by_id",
                table: "wm_monitor_alerts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_module_hour_summaries_created_by_id",
                table: "wm_module_hour_summaries",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_created_by_id",
                table: "wm_approval_requests",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_versions_created_by_id",
                table: "versions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_by_id",
                table: "users",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_dashboards_created_by_id",
                table: "user_dashboards",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_app_releases_created_by_id",
                table: "tray_app_releases",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_created_by_id",
                table: "tenant_subscriptions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_one_time_charges_created_by_id",
                table: "tenant_one_time_charges",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_created_by_id",
                table: "tasks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_statuses_created_by_id",
                table: "task_statuses",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_created_by_id",
                table: "task_status_change_logs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_created_by_id",
                table: "task_percentage_logs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_created_by_id",
                table: "task_edit_logs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_drafts_created_by_id",
                table: "task_drafts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_created_by_id",
                table: "task_comments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_reactions_created_by_id",
                table: "task_comment_reactions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_logs_created_by_id",
                table: "task_comment_logs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_created_by_id",
                table: "task_clocking_sessions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_categories_created_by_id",
                table: "task_categories",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprints_created_by_id",
                table: "sprints",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprint_activity_logs_created_by_id",
                table: "sprint_activity_logs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_created_by_id",
                table: "roles",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_created_by_id",
                table: "reporting_manager_changes",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_created_by_id",
                table: "release_calendar",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_created_by_id",
                table: "projects",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_created_by_id",
                table: "project_members",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_created_by_id",
                table: "project_member_invitations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_categories_created_by_id",
                table: "project_categories",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_created_by_id",
                table: "positions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_created_by_id",
                table: "position_assignments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_planned_task_blocks_created_by_id",
                table: "planned_task_blocks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_created_by_id",
                table: "personal_calendar_events",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_country_routes_created_by_id",
                table: "payment_gateway_country_routes",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_configs_created_by_id",
                table: "payment_gateway_configs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_created_by_id",
                table: "onboarding_drafts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_created_by_id",
                table: "objectives",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_created_by_id",
                table: "meeting_series",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_labels_created_by_id",
                table: "labels",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_decision_journal_created_by_id",
                table: "inbox_decision_journal",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_settings_created_by_id",
                table: "holiday_calendar_settings",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_global_app_catalog_created_by_id",
                table: "global_app_catalog",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_assets_created_by_id",
                table: "entity_assets",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_created_by_id",
                table: "employees",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_emergency_contacts_created_by_id",
                table: "employee_emergency_contacts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_created_by_id",
                table: "employee_dependents",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_bank_details_created_by_id",
                table: "employee_bank_details",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_addresses_created_by_id",
                table: "employee_addresses",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_widgets_created_by_id",
                table: "dashboard_widgets",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_widget_layouts_created_by_id",
                table: "dashboard_widget_layouts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_templates_created_by_id",
                table: "dashboard_templates",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_template_widgets_created_by_id",
                table: "dashboard_template_widgets",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_template_widget_layouts_created_by_id",
                table: "dashboard_template_widget_layouts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_created_by_id",
                table: "conversations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_created_by_id",
                table: "conversation_participants",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_mutes_created_by_id",
                table: "conversation_mutes",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_created_by_id",
                table: "conversation_messages",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_revisions_created_by_id",
                table: "conversation_message_revisions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_reactions_created_by_id",
                table: "conversation_message_reactions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_mentions_created_by_id",
                table: "conversation_message_mentions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_hides_created_by_id",
                table: "conversation_message_hides",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_attachments_created_by_id",
                table: "conversation_message_attachments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_favorites_created_by_id",
                table: "conversation_favorites",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_created_by_id",
                table: "clock_in_policies",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_meeting_incidents_created_by_id",
                table: "calendar_meeting_incidents",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_created_by_id",
                table: "calendar_events",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_reminders_sent_created_by_id",
                table: "calendar_event_reminders_sent",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_created_by_id",
                table: "calendar_event_participants",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participant_rsvp_history_created_by_id",
                table: "calendar_event_participant_rsvp_history",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_created_by_id",
                table: "bulk_onboarding_batches",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_created_by_id",
                table: "bulk_onboarding_batch_rows",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alerts_created_by_id",
                table: "attendance_alerts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alert_deductions_created_by_id",
                table: "attendance_alert_deductions",
                column: "created_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alert_deductions_users_created_by_id",
                table: "attendance_alert_deductions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_alerts_users_created_by_id",
                table: "attendance_alerts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_users_created_by_id",
                table: "bulk_onboarding_batch_rows",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batches_users_created_by_id",
                table: "bulk_onboarding_batches",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participant_rsvp_history_users_created_by_id",
                table: "calendar_event_participant_rsvp_history",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participants_users_created_by_id",
                table: "calendar_event_participants",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_reminders_sent_users_created_by_id",
                table: "calendar_event_reminders_sent",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_events_employees_created_by_id",
                table: "calendar_events",
                column: "created_by_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_meeting_incidents_users_created_by_id",
                table: "calendar_meeting_incidents",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_clock_in_policies_users_created_by_id",
                table: "clock_in_policies",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_favorites_users_created_by_id",
                table: "conversation_favorites",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_attachments_users_created_by_id",
                table: "conversation_message_attachments",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_hides_users_created_by_id",
                table: "conversation_message_hides",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_mentions_users_created_by_id",
                table: "conversation_message_mentions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_reactions_users_created_by_id",
                table: "conversation_message_reactions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_revisions_users_created_by_id",
                table: "conversation_message_revisions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_messages_users_created_by_id",
                table: "conversation_messages",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_mutes_users_created_by_id",
                table: "conversation_mutes",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_participants_users_created_by_id",
                table: "conversation_participants",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_users_created_by_id",
                table: "conversations",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_template_widget_layouts_users_created_by_id",
                table: "dashboard_template_widget_layouts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_template_widgets_users_created_by_id",
                table: "dashboard_template_widgets",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_templates_users_created_by_id",
                table: "dashboard_templates",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_widget_layouts_users_created_by_id",
                table: "dashboard_widget_layouts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_widgets_users_created_by_id",
                table: "dashboard_widgets",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_addresses_users_created_by_id",
                table: "employee_addresses",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_bank_details_users_created_by_id",
                table: "employee_bank_details",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_dependents_users_created_by_id",
                table: "employee_dependents",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_emergency_contacts_users_created_by_id",
                table: "employee_emergency_contacts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_users_created_by_id",
                table: "employees",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_entity_assets_users_created_by_id",
                table: "entity_assets",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_global_app_catalog_platform_users_created_by_id",
                table: "global_app_catalog",
                column: "created_by_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_holiday_calendar_settings_users_created_by_id",
                table: "holiday_calendar_settings",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inbox_decision_journal_users_created_by_id",
                table: "inbox_decision_journal",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_labels_users_created_by_id",
                table: "labels",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_series_users_created_by_id",
                table: "meeting_series",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_objectives_users_created_by_id",
                table: "objectives",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_users_created_by_id",
                table: "onboarding_drafts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_gateway_configs_platform_users_created_by_id",
                table: "payment_gateway_configs",
                column: "created_by_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_gateway_country_routes_platform_users_created_by_id",
                table: "payment_gateway_country_routes",
                column: "created_by_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_personal_calendar_events_users_created_by_id",
                table: "personal_calendar_events",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_planned_task_blocks_users_created_by_id",
                table: "planned_task_blocks",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_position_assignments_users_created_by_id",
                table: "position_assignments",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_positions_users_created_by_id",
                table: "positions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_categories_users_created_by_id",
                table: "project_categories",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_member_invitations_users_created_by_id",
                table: "project_member_invitations",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_project_members_users_created_by_id",
                table: "project_members",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_users_created_by_id",
                table: "projects",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_release_calendar_users_created_by_id",
                table: "release_calendar",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_reporting_manager_changes_users_created_by_id",
                table: "reporting_manager_changes",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_roles_users_created_by_id",
                table: "roles",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sprint_activity_logs_users_created_by_id",
                table: "sprint_activity_logs",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sprints_users_created_by_id",
                table: "sprints",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_categories_users_created_by_id",
                table: "task_categories",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_clocking_sessions_users_created_by_id",
                table: "task_clocking_sessions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_comment_logs_users_created_by_id",
                table: "task_comment_logs",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_comment_reactions_users_created_by_id",
                table: "task_comment_reactions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_comments_users_created_by_id",
                table: "task_comments",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_drafts_users_created_by_id",
                table: "task_drafts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_edit_logs_users_created_by_id",
                table: "task_edit_logs",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_percentage_logs_users_created_by_id",
                table: "task_percentage_logs",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_status_change_logs_users_created_by_id",
                table: "task_status_change_logs",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_statuses_users_created_by_id",
                table: "task_statuses",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_users_created_by_id",
                table: "tasks",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_one_time_charges_users_created_by_id",
                table: "tenant_one_time_charges",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_subscriptions_platform_users_created_by_id",
                table: "tenant_subscriptions",
                column: "created_by_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_app_releases_platform_users_created_by_id",
                table: "tray_app_releases",
                column: "created_by_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_dashboards_users_created_by_id",
                table: "user_dashboards",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_users_created_by_id",
                table: "users",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_versions_users_created_by_id",
                table: "versions",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_approval_requests_users_created_by_id",
                table: "wm_approval_requests",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_module_hour_summaries_users_created_by_id",
                table: "wm_module_hour_summaries",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_monitor_alerts_users_created_by_id",
                table: "wm_monitor_alerts",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_notification_log_users_created_by_id",
                table: "wm_notification_log",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wm_task_rate_settings_users_created_by_id",
                table: "wm_task_rate_settings",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alert_deductions_users_created_by_id",
                table: "attendance_alert_deductions");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_alerts_users_created_by_id",
                table: "attendance_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_users_created_by_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batches_users_created_by_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participant_rsvp_history_users_created_by_id",
                table: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participants_users_created_by_id",
                table: "calendar_event_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_reminders_sent_users_created_by_id",
                table: "calendar_event_reminders_sent");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_events_employees_created_by_id",
                table: "calendar_events");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_meeting_incidents_users_created_by_id",
                table: "calendar_meeting_incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_clock_in_policies_users_created_by_id",
                table: "clock_in_policies");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_favorites_users_created_by_id",
                table: "conversation_favorites");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_attachments_users_created_by_id",
                table: "conversation_message_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_hides_users_created_by_id",
                table: "conversation_message_hides");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_mentions_users_created_by_id",
                table: "conversation_message_mentions");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_reactions_users_created_by_id",
                table: "conversation_message_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_revisions_users_created_by_id",
                table: "conversation_message_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_messages_users_created_by_id",
                table: "conversation_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_mutes_users_created_by_id",
                table: "conversation_mutes");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_participants_users_created_by_id",
                table: "conversation_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_users_created_by_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_template_widget_layouts_users_created_by_id",
                table: "dashboard_template_widget_layouts");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_template_widgets_users_created_by_id",
                table: "dashboard_template_widgets");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_templates_users_created_by_id",
                table: "dashboard_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_widget_layouts_users_created_by_id",
                table: "dashboard_widget_layouts");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_widgets_users_created_by_id",
                table: "dashboard_widgets");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_addresses_users_created_by_id",
                table: "employee_addresses");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_bank_details_users_created_by_id",
                table: "employee_bank_details");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_dependents_users_created_by_id",
                table: "employee_dependents");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_emergency_contacts_users_created_by_id",
                table: "employee_emergency_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_users_created_by_id",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_entity_assets_users_created_by_id",
                table: "entity_assets");

            migrationBuilder.DropForeignKey(
                name: "fk_global_app_catalog_platform_users_created_by_id",
                table: "global_app_catalog");

            migrationBuilder.DropForeignKey(
                name: "fk_holiday_calendar_settings_users_created_by_id",
                table: "holiday_calendar_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_inbox_decision_journal_users_created_by_id",
                table: "inbox_decision_journal");

            migrationBuilder.DropForeignKey(
                name: "fk_labels_users_created_by_id",
                table: "labels");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_series_users_created_by_id",
                table: "meeting_series");

            migrationBuilder.DropForeignKey(
                name: "fk_objectives_users_created_by_id",
                table: "objectives");

            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_users_created_by_id",
                table: "onboarding_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_payment_gateway_configs_platform_users_created_by_id",
                table: "payment_gateway_configs");

            migrationBuilder.DropForeignKey(
                name: "fk_payment_gateway_country_routes_platform_users_created_by_id",
                table: "payment_gateway_country_routes");

            migrationBuilder.DropForeignKey(
                name: "fk_personal_calendar_events_users_created_by_id",
                table: "personal_calendar_events");

            migrationBuilder.DropForeignKey(
                name: "fk_planned_task_blocks_users_created_by_id",
                table: "planned_task_blocks");

            migrationBuilder.DropForeignKey(
                name: "fk_position_assignments_users_created_by_id",
                table: "position_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_positions_users_created_by_id",
                table: "positions");

            migrationBuilder.DropForeignKey(
                name: "fk_project_categories_users_created_by_id",
                table: "project_categories");

            migrationBuilder.DropForeignKey(
                name: "fk_project_member_invitations_users_created_by_id",
                table: "project_member_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_project_members_users_created_by_id",
                table: "project_members");

            migrationBuilder.DropForeignKey(
                name: "fk_projects_users_created_by_id",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_release_calendar_users_created_by_id",
                table: "release_calendar");

            migrationBuilder.DropForeignKey(
                name: "fk_reporting_manager_changes_users_created_by_id",
                table: "reporting_manager_changes");

            migrationBuilder.DropForeignKey(
                name: "fk_roles_users_created_by_id",
                table: "roles");

            migrationBuilder.DropForeignKey(
                name: "fk_sprint_activity_logs_users_created_by_id",
                table: "sprint_activity_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_sprints_users_created_by_id",
                table: "sprints");

            migrationBuilder.DropForeignKey(
                name: "fk_task_categories_users_created_by_id",
                table: "task_categories");

            migrationBuilder.DropForeignKey(
                name: "fk_task_clocking_sessions_users_created_by_id",
                table: "task_clocking_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comment_logs_users_created_by_id",
                table: "task_comment_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comment_reactions_users_created_by_id",
                table: "task_comment_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_task_comments_users_created_by_id",
                table: "task_comments");

            migrationBuilder.DropForeignKey(
                name: "fk_task_drafts_users_created_by_id",
                table: "task_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_task_edit_logs_users_created_by_id",
                table: "task_edit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_percentage_logs_users_created_by_id",
                table: "task_percentage_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_status_change_logs_users_created_by_id",
                table: "task_status_change_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_task_statuses_users_created_by_id",
                table: "task_statuses");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_users_created_by_id",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_one_time_charges_users_created_by_id",
                table: "tenant_one_time_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_subscriptions_platform_users_created_by_id",
                table: "tenant_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_app_releases_platform_users_created_by_id",
                table: "tray_app_releases");

            migrationBuilder.DropForeignKey(
                name: "fk_user_dashboards_users_created_by_id",
                table: "user_dashboards");

            migrationBuilder.DropForeignKey(
                name: "fk_users_users_created_by_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_versions_users_created_by_id",
                table: "versions");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_approval_requests_users_created_by_id",
                table: "wm_approval_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_module_hour_summaries_users_created_by_id",
                table: "wm_module_hour_summaries");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_monitor_alerts_users_created_by_id",
                table: "wm_monitor_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_notification_log_users_created_by_id",
                table: "wm_notification_log");

            migrationBuilder.DropForeignKey(
                name: "fk_wm_task_rate_settings_users_created_by_id",
                table: "wm_task_rate_settings");

            migrationBuilder.DropIndex(
                name: "ix_wm_task_rate_settings_created_by_id",
                table: "wm_task_rate_settings");

            migrationBuilder.DropIndex(
                name: "ix_wm_notification_log_created_by_id",
                table: "wm_notification_log");

            migrationBuilder.DropIndex(
                name: "ix_wm_monitor_alerts_created_by_id",
                table: "wm_monitor_alerts");

            migrationBuilder.DropIndex(
                name: "ix_wm_module_hour_summaries_created_by_id",
                table: "wm_module_hour_summaries");

            migrationBuilder.DropIndex(
                name: "ix_wm_approval_requests_created_by_id",
                table: "wm_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_versions_created_by_id",
                table: "versions");

            migrationBuilder.DropIndex(
                name: "ix_users_created_by_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_user_dashboards_created_by_id",
                table: "user_dashboards");

            migrationBuilder.DropIndex(
                name: "ix_tray_app_releases_created_by_id",
                table: "tray_app_releases");

            migrationBuilder.DropIndex(
                name: "ix_tenant_subscriptions_created_by_id",
                table: "tenant_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_tenant_one_time_charges_created_by_id",
                table: "tenant_one_time_charges");

            migrationBuilder.DropIndex(
                name: "ix_tasks_created_by_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_statuses_created_by_id",
                table: "task_statuses");

            migrationBuilder.DropIndex(
                name: "ix_task_status_change_logs_created_by_id",
                table: "task_status_change_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_percentage_logs_created_by_id",
                table: "task_percentage_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_edit_logs_created_by_id",
                table: "task_edit_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_drafts_created_by_id",
                table: "task_drafts");

            migrationBuilder.DropIndex(
                name: "ix_task_comments_created_by_id",
                table: "task_comments");

            migrationBuilder.DropIndex(
                name: "ix_task_comment_reactions_created_by_id",
                table: "task_comment_reactions");

            migrationBuilder.DropIndex(
                name: "ix_task_comment_logs_created_by_id",
                table: "task_comment_logs");

            migrationBuilder.DropIndex(
                name: "ix_task_clocking_sessions_created_by_id",
                table: "task_clocking_sessions");

            migrationBuilder.DropIndex(
                name: "ix_task_categories_created_by_id",
                table: "task_categories");

            migrationBuilder.DropIndex(
                name: "ix_sprints_created_by_id",
                table: "sprints");

            migrationBuilder.DropIndex(
                name: "ix_sprint_activity_logs_created_by_id",
                table: "sprint_activity_logs");

            migrationBuilder.DropIndex(
                name: "ix_roles_created_by_id",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_reporting_manager_changes_created_by_id",
                table: "reporting_manager_changes");

            migrationBuilder.DropIndex(
                name: "ix_release_calendar_created_by_id",
                table: "release_calendar");

            migrationBuilder.DropIndex(
                name: "ix_projects_created_by_id",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_project_members_created_by_id",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_created_by_id",
                table: "project_member_invitations");

            migrationBuilder.DropIndex(
                name: "ix_project_categories_created_by_id",
                table: "project_categories");

            migrationBuilder.DropIndex(
                name: "ix_positions_created_by_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_position_assignments_created_by_id",
                table: "position_assignments");

            migrationBuilder.DropIndex(
                name: "ix_planned_task_blocks_created_by_id",
                table: "planned_task_blocks");

            migrationBuilder.DropIndex(
                name: "ix_personal_calendar_events_created_by_id",
                table: "personal_calendar_events");

            migrationBuilder.DropIndex(
                name: "ix_payment_gateway_country_routes_created_by_id",
                table: "payment_gateway_country_routes");

            migrationBuilder.DropIndex(
                name: "ix_payment_gateway_configs_created_by_id",
                table: "payment_gateway_configs");

            migrationBuilder.DropIndex(
                name: "ix_onboarding_drafts_created_by_id",
                table: "onboarding_drafts");

            migrationBuilder.DropIndex(
                name: "ix_objectives_created_by_id",
                table: "objectives");

            migrationBuilder.DropIndex(
                name: "ix_meeting_series_created_by_id",
                table: "meeting_series");

            migrationBuilder.DropIndex(
                name: "ix_labels_created_by_id",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "ix_inbox_decision_journal_created_by_id",
                table: "inbox_decision_journal");

            migrationBuilder.DropIndex(
                name: "ix_holiday_calendar_settings_created_by_id",
                table: "holiday_calendar_settings");

            migrationBuilder.DropIndex(
                name: "ix_global_app_catalog_created_by_id",
                table: "global_app_catalog");

            migrationBuilder.DropIndex(
                name: "ix_entity_assets_created_by_id",
                table: "entity_assets");

            migrationBuilder.DropIndex(
                name: "ix_employees_created_by_id",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employee_emergency_contacts_created_by_id",
                table: "employee_emergency_contacts");

            migrationBuilder.DropIndex(
                name: "ix_employee_dependents_created_by_id",
                table: "employee_dependents");

            migrationBuilder.DropIndex(
                name: "ix_employee_bank_details_created_by_id",
                table: "employee_bank_details");

            migrationBuilder.DropIndex(
                name: "ix_employee_addresses_created_by_id",
                table: "employee_addresses");

            migrationBuilder.DropIndex(
                name: "ix_dashboard_widgets_created_by_id",
                table: "dashboard_widgets");

            migrationBuilder.DropIndex(
                name: "ix_dashboard_widget_layouts_created_by_id",
                table: "dashboard_widget_layouts");

            migrationBuilder.DropIndex(
                name: "ix_dashboard_templates_created_by_id",
                table: "dashboard_templates");

            migrationBuilder.DropIndex(
                name: "ix_dashboard_template_widgets_created_by_id",
                table: "dashboard_template_widgets");

            migrationBuilder.DropIndex(
                name: "ix_dashboard_template_widget_layouts_created_by_id",
                table: "dashboard_template_widget_layouts");

            migrationBuilder.DropIndex(
                name: "ix_conversations_created_by_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversation_participants_created_by_id",
                table: "conversation_participants");

            migrationBuilder.DropIndex(
                name: "ix_conversation_mutes_created_by_id",
                table: "conversation_mutes");

            migrationBuilder.DropIndex(
                name: "ix_conversation_messages_created_by_id",
                table: "conversation_messages");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_revisions_created_by_id",
                table: "conversation_message_revisions");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_reactions_created_by_id",
                table: "conversation_message_reactions");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_mentions_created_by_id",
                table: "conversation_message_mentions");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_hides_created_by_id",
                table: "conversation_message_hides");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_attachments_created_by_id",
                table: "conversation_message_attachments");

            migrationBuilder.DropIndex(
                name: "ix_conversation_favorites_created_by_id",
                table: "conversation_favorites");

            migrationBuilder.DropIndex(
                name: "ix_clock_in_policies_created_by_id",
                table: "clock_in_policies");

            migrationBuilder.DropIndex(
                name: "ix_calendar_meeting_incidents_created_by_id",
                table: "calendar_meeting_incidents");

            migrationBuilder.DropIndex(
                name: "ix_calendar_events_created_by_id",
                table: "calendar_events");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_reminders_sent_created_by_id",
                table: "calendar_event_reminders_sent");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participants_created_by_id",
                table: "calendar_event_participants");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participant_rsvp_history_created_by_id",
                table: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batches_created_by_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropIndex(
                name: "ix_bulk_onboarding_batch_rows_created_by_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alerts_created_by_id",
                table: "attendance_alerts");

            migrationBuilder.DropIndex(
                name: "ix_attendance_alert_deductions_created_by_id",
                table: "attendance_alert_deductions");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_task_rate_settings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_notification_log",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_monitor_alerts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_module_hour_summaries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "wm_approval_requests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "versions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "users",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "user_dashboards",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "tenant_one_time_charges",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_statuses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_status_change_logs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_percentage_logs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_edit_logs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_drafts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_comments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_comment_reactions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_comment_logs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_clocking_sessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "task_categories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "sprints",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "sprint_activity_logs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "roles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "reporting_manager_changes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "release_calendar",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "projects",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "project_members",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "project_member_invitations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "project_categories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "positions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "position_assignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "planned_task_blocks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "personal_calendar_events",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "onboarding_drafts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "objectives",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "labels",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "inbox_decision_journal",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "holiday_calendar_settings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "entity_assets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employees",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_emergency_contacts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_dependents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_bank_details",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "employee_addresses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_widgets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_widget_layouts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_templates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_template_widgets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "dashboard_template_widget_layouts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_participants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_mutes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_messages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_revisions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_reactions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_mentions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_hides",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_message_attachments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "conversation_favorites",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_meeting_incidents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_event_reminders_sent",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_event_participants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "calendar_event_participant_rsvp_history",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "bulk_onboarding_batches",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "bulk_onboarding_batch_rows",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "attendance_alerts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_id",
                table: "attendance_alert_deductions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
