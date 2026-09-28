using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveTaskRequestsToWorkApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Copy every old task request (all statuses, so history survives) into wm_approval_requests,
            // keeping the same ids so task_edit_logs.edit_request_id still points at the right row.
            // The old flow allowed several pending edits per task; the new unique index allows one, so
            // only the newest pending edit per task stays pending and older ones become 'stale'.
            // SET LOCAL admin mode: the migrator role is NOBYPASSRLS, so without it RLS hides every row.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, o.project_id, 'task.create', 'task', r.created_task_id,
                       COALESCE(r.payload_json->>'Title', r.payload_json->>'title', 'Task'),
                       r.objective_id, 'hierarchy', COALESCE(r.decided_by_employee_id, o.owner_id),
                       r.requested_by_employee_id,
                       jsonb_build_object('ObjectiveId', r.objective_id) || r.payload_json,
                       r.status, r.decided_by_employee_id, r.decision_comment, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM task_creation_requests r
                JOIN objectives o ON o.id = r.objective_id;

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, t.project_id, 'task.edit', 'task', r.task_id, t.title,
                       COALESCE(t.creator_position_objective_id, t.objective_id), 'hierarchy',
                       COALESCE(r.decided_by_employee_id, o.owner_id), r.requested_by_employee_id,
                       r.payload_json || jsonb_build_object('Reason', r.reason),
                       CASE WHEN r.status = 'pending'
                                 AND ROW_NUMBER() OVER (PARTITION BY r.task_id, r.status ORDER BY r.created_at DESC) > 1
                            THEN 'stale' ELSE r.status END,
                       r.decided_by_employee_id, r.decision_comment, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM task_edit_requests r
                JOIN tasks t ON t.id = r.task_id
                JOIN objectives o ON o.id = t.objective_id;
            ");

            migrationBuilder.DropForeignKey(
                name: "fk_task_edit_logs_task_edit_requests_edit_request_id",
                table: "task_edit_logs");

            migrationBuilder.DropTable(
                name: "task_creation_requests");

            migrationBuilder.DropTable(
                name: "task_edit_requests");

            migrationBuilder.DropIndex(
                name: "ix_task_edit_logs_edit_request_id",
                table: "task_edit_logs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data is not restored: rows live on in wm_approval_requests.
            migrationBuilder.CreateTable(
                name: "task_creation_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_comment = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_creation_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_creation_requests_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_creation_requests_work_tasks_created_task_id",
                        column: x => x.created_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_edit_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_comment = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_edit_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_edit_requests_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_edit_request_id",
                table: "task_edit_logs",
                column: "edit_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_creation_requests_created_task_id",
                table: "task_creation_requests",
                column: "created_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_creation_requests_objective_id",
                table: "task_creation_requests",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_creation_requests_tenant_id_objective_id_status",
                table: "task_creation_requests",
                columns: new[] { "tenant_id", "objective_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_requests_task_id",
                table: "task_edit_requests",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_requests_tenant_id_task_id_status",
                table: "task_edit_requests",
                columns: new[] { "tenant_id", "task_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_edit_logs_task_edit_requests_edit_request_id",
                table: "task_edit_logs",
                column: "edit_request_id",
                principalTable: "task_edit_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
