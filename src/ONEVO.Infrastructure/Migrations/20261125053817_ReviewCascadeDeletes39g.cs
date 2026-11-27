using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewCascadeDeletes39g : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_commands_tray_device_registrations_agent_device_id",
                table: "agent_commands");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_bulk_onboarding_batches_batch_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_objectives_calendar_events_calendar_event_id",
                table: "calendar_event_objectives");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_tasks_calendar_events_calendar_event_id",
                table: "calendar_event_tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_favorites_conversations_conversation_id",
                table: "conversation_favorites");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_messages_conversations_conversation_id",
                table: "conversation_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_mutes_conversations_conversation_id",
                table: "conversation_mutes");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_participants_conversations_conversation_id",
                table: "conversation_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_template_widget_layouts_dashboard_template_widgets_template_widget_id",
                table: "dashboard_template_widget_layouts");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_template_widgets_dashboard_templates_template_id",
                table: "dashboard_template_widgets");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_policy_assignments_leave_policies_leave_policy_id",
                table: "leave_policy_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_policy_versions_leave_policies_leave_policy_id",
                table: "leave_policy_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_approvers_leave_requests_leave_request_id",
                table: "leave_request_approvers");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_day_allocations_leave_requests_leave_request_id",
                table: "leave_request_day_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_documents_leave_requests_leave_request_id",
                table: "leave_request_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_module_catalog_price_history_module_catalog_module_key",
                table: "module_catalog_price_history");

            migrationBuilder.DropForeignKey(
                name: "fk_module_features_module_catalog_module_key",
                table: "module_features");

            migrationBuilder.DropForeignKey(
                name: "fk_module_permission_ownership_module_catalog_module_key",
                table: "module_permission_ownership");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_oauth_app_credentials_platform_oauth_apps_platform_oauth_app_id",
                table: "platform_oauth_app_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_role_permissions_platform_permissions_permission_code",
                table: "platform_role_permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_role_permissions_platform_roles_role_id",
                table: "platform_role_permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_credentials_platform_users_platform_user_id",
                table: "platform_user_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_invites_platform_users_platform_user_id",
                table: "platform_user_invites");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_roles_platform_roles_role_id",
                table: "platform_user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_roles_platform_users_user_id",
                table: "platform_user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_sessions_platform_users_account_id",
                table: "platform_user_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_attachments_support_tickets_ticket_id",
                table: "support_ticket_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_comments_support_tickets_ticket_id",
                table: "support_ticket_comments");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_commands_tray_device_registrations_agent_device_id",
                table: "agent_commands",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_bulk_onboarding_batches_batch_id",
                table: "bulk_onboarding_batch_rows",
                column: "batch_id",
                principalTable: "bulk_onboarding_batches",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_objectives_calendar_events_calendar_event_id",
                table: "calendar_event_objectives",
                column: "calendar_event_id",
                principalTable: "calendar_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_tasks_calendar_events_calendar_event_id",
                table: "calendar_event_tasks",
                column: "calendar_event_id",
                principalTable: "calendar_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_favorites_conversations_conversation_id",
                table: "conversation_favorites",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_messages_conversations_conversation_id",
                table: "conversation_messages",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_mutes_conversations_conversation_id",
                table: "conversation_mutes",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_participants_conversations_conversation_id",
                table: "conversation_participants",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_template_widget_layouts_dashboard_template_widgets_template_widget_id",
                table: "dashboard_template_widget_layouts",
                column: "template_widget_id",
                principalTable: "dashboard_template_widgets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_template_widgets_dashboard_templates_template_id",
                table: "dashboard_template_widgets",
                column: "template_id",
                principalTable: "dashboard_templates",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_policy_assignments_leave_policies_leave_policy_id",
                table: "leave_policy_assignments",
                column: "leave_policy_id",
                principalTable: "leave_policies",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_policy_versions_leave_policies_leave_policy_id",
                table: "leave_policy_versions",
                column: "leave_policy_id",
                principalTable: "leave_policies",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_approvers_leave_requests_leave_request_id",
                table: "leave_request_approvers",
                column: "leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_day_allocations_leave_requests_leave_request_id",
                table: "leave_request_day_allocations",
                column: "leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_documents_leave_requests_leave_request_id",
                table: "leave_request_documents",
                column: "leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_module_catalog_price_history_module_catalog_module_key",
                table: "module_catalog_price_history",
                column: "module_key",
                principalTable: "module_catalog",
                principalColumn: "module_key");

            migrationBuilder.AddForeignKey(
                name: "fk_module_features_module_catalog_module_key",
                table: "module_features",
                column: "module_key",
                principalTable: "module_catalog",
                principalColumn: "module_key");

            migrationBuilder.AddForeignKey(
                name: "fk_module_permission_ownership_module_catalog_module_key",
                table: "module_permission_ownership",
                column: "module_key",
                principalTable: "module_catalog",
                principalColumn: "module_key");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_oauth_app_credentials_platform_oauth_apps_platform_oauth_app_id",
                table: "platform_oauth_app_credentials",
                column: "platform_oauth_app_id",
                principalTable: "platform_oauth_apps",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_role_permissions_platform_permissions_permission_code",
                table: "platform_role_permissions",
                column: "permission_code",
                principalTable: "platform_permissions",
                principalColumn: "code");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_role_permissions_platform_roles_role_id",
                table: "platform_role_permissions",
                column: "role_id",
                principalTable: "platform_roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_credentials_platform_users_platform_user_id",
                table: "platform_user_credentials",
                column: "platform_user_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_invites_platform_users_platform_user_id",
                table: "platform_user_invites",
                column: "platform_user_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_roles_platform_roles_role_id",
                table: "platform_user_roles",
                column: "role_id",
                principalTable: "platform_roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_roles_platform_users_user_id",
                table: "platform_user_roles",
                column: "user_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_sessions_platform_users_account_id",
                table: "platform_user_sessions",
                column: "account_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_attachments_support_tickets_ticket_id",
                table: "support_ticket_attachments",
                column: "ticket_id",
                principalTable: "support_tickets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_comments_support_tickets_ticket_id",
                table: "support_ticket_comments",
                column: "ticket_id",
                principalTable: "support_tickets",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_commands_tray_device_registrations_agent_device_id",
                table: "agent_commands");

            migrationBuilder.DropForeignKey(
                name: "fk_bulk_onboarding_batch_rows_bulk_onboarding_batches_batch_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_objectives_calendar_events_calendar_event_id",
                table: "calendar_event_objectives");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_tasks_calendar_events_calendar_event_id",
                table: "calendar_event_tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_favorites_conversations_conversation_id",
                table: "conversation_favorites");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_messages_conversations_conversation_id",
                table: "conversation_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_mutes_conversations_conversation_id",
                table: "conversation_mutes");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_participants_conversations_conversation_id",
                table: "conversation_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_template_widget_layouts_dashboard_template_widgets_template_widget_id",
                table: "dashboard_template_widget_layouts");

            migrationBuilder.DropForeignKey(
                name: "fk_dashboard_template_widgets_dashboard_templates_template_id",
                table: "dashboard_template_widgets");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_policy_assignments_leave_policies_leave_policy_id",
                table: "leave_policy_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_policy_versions_leave_policies_leave_policy_id",
                table: "leave_policy_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_approvers_leave_requests_leave_request_id",
                table: "leave_request_approvers");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_day_allocations_leave_requests_leave_request_id",
                table: "leave_request_day_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_leave_request_documents_leave_requests_leave_request_id",
                table: "leave_request_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_module_catalog_price_history_module_catalog_module_key",
                table: "module_catalog_price_history");

            migrationBuilder.DropForeignKey(
                name: "fk_module_features_module_catalog_module_key",
                table: "module_features");

            migrationBuilder.DropForeignKey(
                name: "fk_module_permission_ownership_module_catalog_module_key",
                table: "module_permission_ownership");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_oauth_app_credentials_platform_oauth_apps_platform_oauth_app_id",
                table: "platform_oauth_app_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_role_permissions_platform_permissions_permission_code",
                table: "platform_role_permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_role_permissions_platform_roles_role_id",
                table: "platform_role_permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_credentials_platform_users_platform_user_id",
                table: "platform_user_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_invites_platform_users_platform_user_id",
                table: "platform_user_invites");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_roles_platform_roles_role_id",
                table: "platform_user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_roles_platform_users_user_id",
                table: "platform_user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_sessions_platform_users_account_id",
                table: "platform_user_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_attachments_support_tickets_ticket_id",
                table: "support_ticket_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_support_ticket_comments_support_tickets_ticket_id",
                table: "support_ticket_comments");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_commands_tray_device_registrations_agent_device_id",
                table: "agent_commands",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_bulk_onboarding_batches_batch_id",
                table: "bulk_onboarding_batch_rows",
                column: "batch_id",
                principalTable: "bulk_onboarding_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_objectives_calendar_events_calendar_event_id",
                table: "calendar_event_objectives",
                column: "calendar_event_id",
                principalTable: "calendar_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_tasks_calendar_events_calendar_event_id",
                table: "calendar_event_tasks",
                column: "calendar_event_id",
                principalTable: "calendar_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_favorites_conversations_conversation_id",
                table: "conversation_favorites",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_messages_conversations_conversation_id",
                table: "conversation_messages",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_mutes_conversations_conversation_id",
                table: "conversation_mutes",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_participants_conversations_conversation_id",
                table: "conversation_participants",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_template_widget_layouts_dashboard_template_widgets_template_widget_id",
                table: "dashboard_template_widget_layouts",
                column: "template_widget_id",
                principalTable: "dashboard_template_widgets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_dashboard_template_widgets_dashboard_templates_template_id",
                table: "dashboard_template_widgets",
                column: "template_id",
                principalTable: "dashboard_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_policy_assignments_leave_policies_leave_policy_id",
                table: "leave_policy_assignments",
                column: "leave_policy_id",
                principalTable: "leave_policies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_policy_versions_leave_policies_leave_policy_id",
                table: "leave_policy_versions",
                column: "leave_policy_id",
                principalTable: "leave_policies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_approvers_leave_requests_leave_request_id",
                table: "leave_request_approvers",
                column: "leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_day_allocations_leave_requests_leave_request_id",
                table: "leave_request_day_allocations",
                column: "leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_request_documents_leave_requests_leave_request_id",
                table: "leave_request_documents",
                column: "leave_request_id",
                principalTable: "leave_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_module_catalog_price_history_module_catalog_module_key",
                table: "module_catalog_price_history",
                column: "module_key",
                principalTable: "module_catalog",
                principalColumn: "module_key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_module_features_module_catalog_module_key",
                table: "module_features",
                column: "module_key",
                principalTable: "module_catalog",
                principalColumn: "module_key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_module_permission_ownership_module_catalog_module_key",
                table: "module_permission_ownership",
                column: "module_key",
                principalTable: "module_catalog",
                principalColumn: "module_key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_oauth_app_credentials_platform_oauth_apps_platform_oauth_app_id",
                table: "platform_oauth_app_credentials",
                column: "platform_oauth_app_id",
                principalTable: "platform_oauth_apps",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_role_permissions_platform_permissions_permission_code",
                table: "platform_role_permissions",
                column: "permission_code",
                principalTable: "platform_permissions",
                principalColumn: "code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_role_permissions_platform_roles_role_id",
                table: "platform_role_permissions",
                column: "role_id",
                principalTable: "platform_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_credentials_platform_users_platform_user_id",
                table: "platform_user_credentials",
                column: "platform_user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_invites_platform_users_platform_user_id",
                table: "platform_user_invites",
                column: "platform_user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_roles_platform_roles_role_id",
                table: "platform_user_roles",
                column: "role_id",
                principalTable: "platform_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_roles_platform_users_user_id",
                table: "platform_user_roles",
                column: "user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_sessions_platform_users_account_id",
                table: "platform_user_sessions",
                column: "account_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_attachments_support_tickets_ticket_id",
                table: "support_ticket_attachments",
                column: "ticket_id",
                principalTable: "support_tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_support_ticket_comments_support_tickets_ticket_id",
                table: "support_ticket_comments",
                column: "ticket_id",
                principalTable: "support_tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
