using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleHourSummaries : Migration
    {
        private static readonly string[] TenantTables = ["wm_module_hour_summaries"];

        // Recomputes ONE Module's row from its own top-level, non-deleted tasks. Buckets follow
        // TaskProgressClassifier without the date rule: Completed = MarksTaskComplete status or 100%,
        // In progress = above 0%, otherwise Not started. Upsert keyed by the Module id.
        private const string RefreshFunction = """
            CREATE OR REPLACE FUNCTION wm_refresh_module_hour_summary(p_objective_id uuid) RETURNS void
            LANGUAGE sql AS $fn$
                INSERT INTO wm_module_hour_summaries AS s
                    (id, tenant_id, project_id, not_started_hours, in_progress_hours, completed_hours,
                     task_count, unestimated_task_count, earliest_open_due_date,
                     created_at, updated_at, created_by_id, is_deleted)
                SELECT
                    o.id, o.tenant_id, o.project_id,
                    COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.done AND t.progress_percent <= 0), 0),
                    COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.done AND t.progress_percent > 0), 0),
                    COALESCE(SUM(t.estimated_hours) FILTER (WHERE x.done), 0),
                    COUNT(t.id),
                    COUNT(t.id) FILTER (WHERE COALESCE(t.estimated_hours, 0) = 0),
                    MIN(t.due_date) FILTER (WHERE NOT x.done),
                    now(), now(), '00000000-0000-0000-0000-000000000000', false
                FROM objectives o
                LEFT JOIN tasks t
                    ON t.objective_id = o.id AND t.tenant_id = o.tenant_id
                    AND t.parent_task_id IS NULL AND NOT t.is_deleted
                LEFT JOIN task_statuses st ON st.id = t.status_id
                CROSS JOIN LATERAL (
                    SELECT (COALESCE(st.marks_task_complete, false) OR COALESCE(t.progress_percent, 0) >= 100) AS done
                ) x
                WHERE o.id = p_objective_id
                GROUP BY o.id, o.tenant_id, o.project_id
                ON CONFLICT (id) DO UPDATE SET
                    not_started_hours = EXCLUDED.not_started_hours,
                    in_progress_hours = EXCLUDED.in_progress_hours,
                    completed_hours = EXCLUDED.completed_hours,
                    task_count = EXCLUDED.task_count,
                    unestimated_task_count = EXCLUDED.unestimated_task_count,
                    earliest_open_due_date = EXCLUDED.earliest_open_due_date,
                    updated_at = now();
            $fn$;
            """;

        private const string TaskTriggers = """
            CREATE OR REPLACE FUNCTION wm_tasks_refresh_module_hour_summary() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP <> 'DELETE' THEN
                    PERFORM wm_refresh_module_hour_summary(NEW.objective_id);
                END IF;
                IF TG_OP = 'DELETE' OR (TG_OP = 'UPDATE' AND OLD.objective_id IS DISTINCT FROM NEW.objective_id) THEN
                    PERFORM wm_refresh_module_hour_summary(OLD.objective_id);
                END IF;
                RETURN NULL;
            END;
            $fn$;

            CREATE TRIGGER trg_tasks_module_hour_summary_insert_delete
                AFTER INSERT OR DELETE ON tasks
                FOR EACH ROW EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary();

            -- Only the columns the summary depends on; title/description edits never recompute.
            CREATE TRIGGER trg_tasks_module_hour_summary_update
                AFTER UPDATE ON tasks
                FOR EACH ROW
                WHEN (OLD.objective_id IS DISTINCT FROM NEW.objective_id
                    OR OLD.status_id IS DISTINCT FROM NEW.status_id
                    OR OLD.estimated_hours IS DISTINCT FROM NEW.estimated_hours
                    OR OLD.progress_percent IS DISTINCT FROM NEW.progress_percent
                    OR OLD.due_date IS DISTINCT FROM NEW.due_date
                    OR OLD.parent_task_id IS DISTINCT FROM NEW.parent_task_id
                    OR OLD.is_deleted IS DISTINCT FROM NEW.is_deleted)
                EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary();

            CREATE OR REPLACE FUNCTION wm_task_statuses_refresh_module_hour_summary() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                PERFORM wm_refresh_module_hour_summary(m.objective_id)
                FROM (SELECT DISTINCT objective_id FROM tasks WHERE status_id = NEW.id) m;
                RETURN NULL;
            END;
            $fn$;

            CREATE TRIGGER trg_task_statuses_module_hour_summary
                AFTER UPDATE ON task_statuses
                FOR EACH ROW
                WHEN (OLD.marks_task_complete IS DISTINCT FROM NEW.marks_task_complete)
                EXECUTE FUNCTION wm_task_statuses_refresh_module_hour_summary();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wm_module_hour_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    not_started_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    in_progress_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    completed_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    task_count = table.Column<int>(type: "integer", nullable: false),
                    unestimated_task_count = table.Column<int>(type: "integer", nullable: false),
                    earliest_open_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_module_hour_summaries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wm_module_hour_summaries_tenant_id_project_id",
                table: "wm_module_hour_summaries",
                columns: new[] { "tenant_id", "project_id" });

            // Tenant isolation, same policy as the other wm_* tables. The triggers run in the
            // writer's tenant context and stamp the Module's own tenant_id, so WITH CHECK holds.
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

            migrationBuilder.Sql(RefreshFunction);
            migrationBuilder.Sql(TaskTriggers);

            // Backfill every existing Module across tenants (admin mode, local to this transaction).
            migrationBuilder.Sql("""
                SELECT set_config('app.tenant_context_mode', 'admin', true);
                SELECT wm_refresh_module_hour_summary(id) FROM objectives;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_task_statuses_module_hour_summary ON task_statuses;
                DROP TRIGGER IF EXISTS trg_tasks_module_hour_summary_update ON tasks;
                DROP TRIGGER IF EXISTS trg_tasks_module_hour_summary_insert_delete ON tasks;
                DROP FUNCTION IF EXISTS wm_task_statuses_refresh_module_hour_summary();
                DROP FUNCTION IF EXISTS wm_tasks_refresh_module_hour_summary();
                DROP FUNCTION IF EXISTS wm_refresh_module_hour_summary(uuid);
                """);

            migrationBuilder.DropTable(
                name: "wm_module_hour_summaries");
        }
    }
}
