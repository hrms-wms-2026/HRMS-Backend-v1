using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Adds Category (not_started/active/done) and Color to task_statuses, then backfills every
    /// existing row and repairs any (tenant, project, objective) scope left without an Active
    /// category row - see docs/superpowers/specs/2026-09-18-task-status-groups-design.md for the
    /// heuristic. SET LOCAL app.tenant_context_mode = 'admin' is required: task_statuses is FORCE
    /// ROW LEVEL SECURITY and migrations run as onevo_migrator (NOSUPERUSER NOBYPASSRLS) - without
    /// it every statement below silently matches zero rows (see BackfillTrayEmployeeIdentity's
    /// migration comment for the empirical verification behind this requirement).
    /// </summary>
    public partial class AddTaskStatusCategoryAndColor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "task_statuses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "task_statuses",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                -- Only live rows choose the scope's single Done row. Prefer an existing
                -- completion flag; historical duplicates and tied orders are resolved by id.
                WITH ranked AS (
                    SELECT id, row_number() OVER (
                        PARTITION BY tenant_id, project_id, objective_id
                        ORDER BY marks_task_complete DESC, display_order DESC, id) AS position
                    FROM task_statuses WHERE NOT is_deleted
                )
                UPDATE task_statuses s SET category = 'done', color = '#16A34A'
                FROM ranked r WHERE s.id = r.id AND r.position = 1;

                WITH ranked AS (
                    SELECT id, row_number() OVER (
                        PARTITION BY tenant_id, project_id, objective_id
                        ORDER BY display_order, id) AS position
                    FROM task_statuses WHERE NOT is_deleted AND category IS NULL
                )
                UPDATE task_statuses s SET category = 'not_started', color = '#94A3B8'
                FROM ranked r WHERE s.id = r.id AND r.position = 1;

                UPDATE task_statuses SET category = 'active', color = '#2563EB'
                WHERE NOT is_deleted AND category IS NULL;

                -- Deleted rows still need non-null columns but cannot satisfy live invariants.
                UPDATE task_statuses
                SET category = CASE WHEN marks_task_complete THEN 'done' ELSE 'not_started' END,
                    color = CASE WHEN marks_task_complete THEN '#16A34A' ELSE '#94A3B8' END
                WHERE is_deleted;
                UPDATE task_statuses SET marks_task_complete = (category = 'done');

                -- A collision must not skip repair. Select the first unused suffix, including
                -- deleted names because the existing unique name index covers physical rows.
                INSERT INTO task_statuses (
                    id, tenant_id, project_id, objective_id, name, display_order,
                    requires_approval, approver_id, marks_task_complete, visibility,
                    category, color, created_at, created_by_id, is_deleted
                )
                SELECT gen_random_uuid(), scope.tenant_id, scope.project_id, scope.objective_id,
                    available.name, scope.max_order + 1,
                    false, NULL, false, 'public', 'active', '#2563EB', now(),
                    (SELECT s.created_by_id FROM task_statuses s
                     WHERE s.tenant_id = scope.tenant_id AND s.project_id = scope.project_id
                       AND s.objective_id IS NOT DISTINCT FROM scope.objective_id AND NOT s.is_deleted
                     ORDER BY s.display_order, s.id LIMIT 1), false
                FROM (
                    SELECT tenant_id, project_id, objective_id, MAX(display_order) AS max_order,
                        COUNT(*) AS row_count
                    FROM task_statuses
                    GROUP BY tenant_id, project_id, objective_id
                    HAVING bool_or(NOT is_deleted)
                ) scope
                CROSS JOIN LATERAL (
                    SELECT CASE WHEN n = 0 THEN 'In Progress'
                        ELSE 'In Progress (' || n || ')' END AS name
                    FROM generate_series(0, scope.row_count) n
                    WHERE NOT EXISTS (
                        SELECT 1 FROM task_statuses s
                        WHERE s.tenant_id = scope.tenant_id AND s.project_id = scope.project_id
                          AND s.objective_id IS NOT DISTINCT FROM scope.objective_id
                          AND s.name = CASE WHEN n = 0 THEN 'In Progress'
                              ELSE 'In Progress (' || n || ')' END)
                    ORDER BY n LIMIT 1
                ) available
                WHERE NOT EXISTS (
                    SELECT 1 FROM task_statuses s
                    WHERE s.tenant_id = scope.tenant_id AND s.project_id = scope.project_id
                      AND s.objective_id IS NOT DISTINCT FROM scope.objective_id
                      AND NOT s.is_deleted AND s.category = 'active'
                );
            """);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "task_statuses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "not_started",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "task_statuses",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#94A3B8",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "color", table: "task_statuses");
            migrationBuilder.DropColumn(name: "category", table: "task_statuses");
        }
    }
}

