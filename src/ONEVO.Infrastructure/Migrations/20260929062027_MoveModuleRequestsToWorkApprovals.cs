using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveModuleRequestsToWorkApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Copy every module change request (all statuses) into wm_approval_requests, ids preserved
            // (old bell notifications still point at them). Old rows put the authenticated UserId in
            // decided_by_id, so the decider is taken from reporting_manager_id (the only employee the
            // old approve/reject commands let act).
            // SET LOCAL admin mode: the migrator role is NOBYPASSRLS, so without it RLS hides every row.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, o.project_id,
                       CASE r.request_type WHEN 'extend_allocation' THEN 'module.allocation_extend'
                                           ELSE 'module.' || r.request_type END,
                       'module', r.objective_id, o.title,
                       COALESCE(o.creator_position_objective_id, o.parent_objective_id), 'hierarchy',
                       r.reporting_manager_id, r.requested_by_id,
                       COALESCE(r.payload_json, '{}'::jsonb),
                       r.status,
                       CASE WHEN r.status = 'pending' THEN NULL ELSE r.reporting_manager_id END,
                       NULL, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM objective_change_requests r
                JOIN objectives o ON o.id = r.objective_id;
            ");

            migrationBuilder.DropTable(
                name: "objective_change_requests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data is not restored: rows live on in wm_approval_requests.
            migrationBuilder.CreateTable(
                name: "objective_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    reporting_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objective_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_objective_change_requests_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_objective_id",
                table: "objective_change_requests",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_one_pending_per_objective",
                table: "objective_change_requests",
                columns: new[] { "tenant_id", "objective_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_tenant_id_objective_id_status",
                table: "objective_change_requests",
                columns: new[] { "tenant_id", "objective_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_tenant_id_reporting_manager_id_status",
                table: "objective_change_requests",
                columns: new[] { "tenant_id", "reporting_manager_id", "status" });
        }
    }
}
