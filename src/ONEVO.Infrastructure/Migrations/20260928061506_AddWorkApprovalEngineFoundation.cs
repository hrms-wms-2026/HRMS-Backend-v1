using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkApprovalEngineFoundation : Migration
    {
        private static readonly string[] TenantTables =
        [
            "wm_approval_requests",
            "wm_notification_log"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "creator_position_objective_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creator_position_objective_id",
                table: "sprints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creator_position_objective_id",
                table: "objectives",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wm_approval_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    position_objective_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approver_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_comment = table.Column<string>(type: "text", nullable: true),
                    target_updated_at_snapshot = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wm_notification_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_notification_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_tenant_id_approver_employee_id_status",
                table: "wm_approval_requests",
                columns: new[] { "tenant_id", "approver_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_tenant_id_project_id_status",
                table: "wm_approval_requests",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_wm_approval_requests_one_pending_per_target_action",
                table: "wm_approval_requests",
                columns: new[] { "tenant_id", "target_type", "target_id", "action_type" },
                unique: true,
                filter: "status = 'pending' AND target_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_tenant_id_project_id_recipient_created_at",
                table: "wm_notification_log",
                columns: new[] { "tenant_id", "project_id", "recipient_employee_id", "created_at" },
                descending: new[] { false, false, false, true });

            // Backfill creator positions (spec §4). onevo_migrator is NOBYPASSRLS, so each statement
            // must open admin tenant context in the same batch or it silently updates zero rows.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE objectives
                SET creator_position_objective_id = parent_objective_id
                WHERE creator_position_objective_id IS NULL AND parent_objective_id IS NOT NULL;

                WITH RECURSIVE chain AS (
                    SELECT t.id AS task_id, o.id AS module_id, o.parent_objective_id, o.owner_id, 0 AS depth
                    FROM tasks t JOIN objectives o ON o.id = t.objective_id
                    UNION ALL
                    SELECT c.task_id, p.id, p.parent_objective_id, p.owner_id, c.depth + 1
                    FROM chain c JOIN objectives p ON p.id = c.parent_objective_id
                    WHERE c.depth < 64
                ),
                owned AS (
                    SELECT DISTINCT ON (c.task_id) c.task_id, c.module_id
                    FROM chain c
                    JOIN tasks t ON t.id = c.task_id
                    JOIN employees e ON e.user_id = t.created_by_id AND e.tenant_id = t.tenant_id AND e.id = c.owner_id
                    ORDER BY c.task_id, c.depth DESC
                )
                UPDATE tasks t
                SET creator_position_objective_id = COALESCE(owned.module_id, t.objective_id)
                FROM tasks t2 LEFT JOIN owned ON owned.task_id = t2.id
                WHERE t.id = t2.id AND t.creator_position_objective_id IS NULL;

                WITH RECURSIVE depths AS (
                    SELECT o.id, o.project_id, o.owner_id, 0 AS depth
                    FROM objectives o WHERE o.parent_objective_id IS NULL
                    UNION ALL
                    SELECT c.id, c.project_id, c.owner_id, d.depth + 1
                    FROM objectives c JOIN depths d ON c.parent_objective_id = d.id
                    WHERE d.depth < 64
                ),
                highest AS (
                    SELECT DISTINCT ON (s.id) s.id AS sprint_id, d.id AS module_id
                    FROM sprints s
                    JOIN employees e ON e.user_id = s.created_by_id AND e.tenant_id = s.tenant_id
                    JOIN depths d ON d.project_id = s.project_id AND d.owner_id = e.id
                    ORDER BY s.id, d.depth ASC
                ),
                roots AS (
                    SELECT project_id, id AS module_id FROM objectives WHERE is_default = true
                )
                UPDATE sprints s
                SET creator_position_objective_id = COALESCE(highest.module_id, roots.module_id)
                FROM sprints s2
                LEFT JOIN highest ON highest.sprint_id = s2.id
                LEFT JOIN roots ON roots.project_id = s2.project_id
                WHERE s.id = s2.id AND s.creator_position_objective_id IS NULL;
            ");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(
                name: "wm_approval_requests");

            migrationBuilder.DropTable(
                name: "wm_notification_log");

            migrationBuilder.DropColumn(
                name: "creator_position_objective_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "creator_position_objective_id",
                table: "sprints");

            migrationBuilder.DropColumn(
                name: "creator_position_objective_id",
                table: "objectives");
        }
    }
}
