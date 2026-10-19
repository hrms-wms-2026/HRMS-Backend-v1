using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLeaveRequestInfoAndForward : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// "Ask for information" and "Send to (manager)" were removed from leave approvals. Requests and approver rows
        /// waiting for an answer go back to pending (the approver decides as normal); forwarded approver rows become
        /// skipped (the person it was sent to keeps their own pending row). The question/answer records, the journal
        /// entries, chat tags, notification templates/notifications/rules and queued emails of those actions are
        /// deleted. All tenants: SET LOCAL lifts tenant row security for this migration's own transaction only.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE leave_requests SET status = 'pending' WHERE status = 'information_requested';
                UPDATE leave_request_approvers SET status = 'pending' WHERE status = 'information_requested';
                UPDATE leave_request_approvers SET status = 'skipped' WHERE status = 'forwarded';

                DELETE FROM inbox_decision_journal
                WHERE source = 'leave' AND action IN ('forward', 'request_info', 'respond_info');

                UPDATE conversation_messages SET tag = NULL WHERE tag IN ('info_request', 'info_response');

                DELETE FROM outbox_messages WHERE type = 'leave_information_requested';

                DELETE FROM notifications WHERE template_code = 'leave_request_information_requested';
                DELETE FROM notification_deliveries WHERE event_key = 'leave_request_information_requested';
                DELETE FROM tenant_notification_rules WHERE event_key = 'leave_request_information_requested';
                UPDATE notification_templates SET active_version_id = NULL
                WHERE code = 'leave_request_information_requested' OR event_key = 'leave_request_information_requested';
                DELETE FROM notification_template_versions WHERE template_id IN (
                    SELECT id FROM notification_templates
                    WHERE code = 'leave_request_information_requested' OR event_key = 'leave_request_information_requested');
                DELETE FROM notification_templates
                WHERE code = 'leave_request_information_requested' OR event_key = 'leave_request_information_requested';
                """);

            migrationBuilder.DropTable(
                name: "leave_request_info_messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_request_info_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sender_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_info_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_info_messages_employees_sender_employee_id",
                        column: x => x.sender_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_request_info_messages_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_info_messages_leave_request_id",
                table: "leave_request_info_messages",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_info_messages_sender_employee_id",
                table: "leave_request_info_messages",
                column: "sender_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_info_messages_tenant_request_created",
                table: "leave_request_info_messages",
                columns: new[] { "tenant_id", "leave_request_id", "created_at" });
        }
    }
}
