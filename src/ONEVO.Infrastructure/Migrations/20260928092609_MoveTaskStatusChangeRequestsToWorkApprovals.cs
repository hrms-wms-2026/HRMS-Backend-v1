using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveTaskStatusChangeRequestsToWorkApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Copy every status-template request (all statuses, ids preserved so old bell
            // notifications still navigate) into wm_approval_requests. 'outdated' becomes 'stale'.
            // Position/approver = the project's root (default) module and its owner.
            // SET LOCAL admin mode: the migrator role is NOBYPASSRLS, so without it RLS hides every row.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, r.project_id, 'project.status_template_change', 'project', NULL,
                       COALESCE(p.name, 'Task statuses'),
                       root.id, 'hierarchy',
                       COALESCE(r.decided_by_employee_id, root.owner_id, r.requested_by_employee_id),
                       r.requested_by_employee_id,
                       jsonb_build_object('Changes', r.changes_json, 'Note', r.note),
                       CASE WHEN r.status = 'outdated' THEN 'stale' ELSE r.status END,
                       r.decided_by_employee_id, r.decision_comment, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM task_status_change_requests r
                LEFT JOIN projects p ON p.id = r.project_id
                LEFT JOIN LATERAL (
                    SELECT o.id, o.owner_id FROM objectives o
                    WHERE o.project_id = r.project_id AND o.is_default AND o.parent_objective_id IS NULL
                    ORDER BY o.is_deleted, o.created_at
                    LIMIT 1
                ) root ON true;
            ");

            migrationBuilder.DropTable(
                name: "task_status_change_requests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data is not restored: rows live on in wm_approval_requests.
            migrationBuilder.CreateTable(
                name: "task_status_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_comment = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_status_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_status_change_requests_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_requests_project_id",
                table: "task_status_change_requests",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_requests_tenant_id_project_id_status",
                table: "task_status_change_requests",
                columns: new[] { "tenant_id", "project_id", "status" });
        }
    }
}
