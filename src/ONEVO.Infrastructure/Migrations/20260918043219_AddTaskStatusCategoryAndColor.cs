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

                -- Done: the row already flagged complete in its scope.
                UPDATE task_statuses d
                SET category = 'done', color = '#16A34A'
                WHERE d.marks_task_complete = true;

                -- Fallback Done: a scope with no marks_task_complete row at all (pre-dates the
                -- "exactly one complete status" invariant enforced by ReorderTaskStatuses) - take
                -- the highest DisplayOrder row in that scope.
                UPDATE task_statuses d
                SET category = 'done', color = '#16A34A'
                WHERE d.category IS NULL
                  AND d.display_order = (
                      SELECT MAX(s.display_order) FROM task_statuses s
                      WHERE s.tenant_id = d.tenant_id AND s.project_id = d.project_id
                        AND s.objective_id IS NOT DISTINCT FROM d.objective_id
                  );

                -- Not Started: the lowest-DisplayOrder row remaining in each scope.
                UPDATE task_statuses n
                SET category = 'not_started', color = '#94A3B8'
                WHERE n.category IS NULL
                  AND n.display_order = (
                      SELECT MIN(s.display_order) FROM task_statuses s
                      WHERE s.tenant_id = n.tenant_id AND s.project_id = n.project_id
                        AND s.objective_id IS NOT DISTINCT FROM n.objective_id
                        AND s.category IS NULL
                  );

                -- Active: everything else remaining.
                UPDATE task_statuses a
                SET category = 'active', color = '#2563EB'
                WHERE a.category IS NULL;

                -- Repair pass: any scope left with zero Active rows (e.g. a project that only ever
                -- had 2 statuses) gets a synthetic "In Progress" Active row, so clock-in's "find
                -- first Active status" can never come up empty. Guarded against the unique
                -- (tenant_id, project_id, objective_id, name) index too, in case a row named
                -- "In Progress" already exists in that scope under a different category.
                INSERT INTO task_statuses (
                    id, tenant_id, project_id, objective_id, name, display_order,
                    requires_approval, approver_id, marks_task_complete, visibility,
                    category, color, created_at, created_by_id, is_deleted
                )
                SELECT
                    gen_random_uuid(), scope.tenant_id, scope.project_id, scope.objective_id,
                    'In Progress', scope.max_order + 1,
                    false, NULL, false, 'public', 'active', '#2563EB', now(),
                    (SELECT s.created_by_id FROM task_statuses s
                     WHERE s.tenant_id = scope.tenant_id AND s.project_id = scope.project_id
                       AND s.objective_id IS NOT DISTINCT FROM scope.objective_id
                     ORDER BY s.display_order LIMIT 1),
                    false
                FROM (
                    SELECT tenant_id, project_id, objective_id, MAX(display_order) AS max_order
                    FROM task_statuses
                    GROUP BY tenant_id, project_id, objective_id
                ) scope
                WHERE NOT EXISTS (
                    SELECT 1 FROM task_statuses s3
                    WHERE s3.tenant_id = scope.tenant_id AND s3.project_id = scope.project_id
                      AND s3.objective_id IS NOT DISTINCT FROM scope.objective_id
                      AND s3.category = 'active'
                )
                AND NOT EXISTS (
                    SELECT 1 FROM task_statuses s4
                    WHERE s4.tenant_id = scope.tenant_id AND s4.project_id = scope.project_id
                      AND s4.objective_id IS NOT DISTINCT FROM scope.objective_id
                      AND s4.name = 'In Progress'
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
