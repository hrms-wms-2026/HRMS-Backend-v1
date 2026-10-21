using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Excludes meeting occurrences from the module-hour summaries that Team Progress reads.
    /// <para>
    /// A meeting occurrence is an ordinary row in <c>tasks</c> (task_kind = 'meeting') plus a
    /// 1:1 <c>meeting_details</c> row. EF Core hides those rows from every ordinary task query
    /// through the named <c>ExcludeMeetings</c> filter, but <c>wm_refresh_module_hour_summary</c>
    /// is raw SQL and therefore never sees that filter: its join on <c>tasks</c> had no
    /// <c>task_kind</c> predicate, so every meeting occurrence inflated
    /// <c>task_count</c> and <c>unestimated_task_count</c> (a meeting occurrence carries no
    /// estimated_hours, so it also landed in the "unestimated" bucket and moved
    /// <c>earliest_open_due_date</c>). The hour buckets were unaffected only because a meeting
    /// occurrence has a NULL estimate.
    /// </para>
    /// <para>
    /// The fix is one added predicate on the join:
    /// <c>AND t.task_kind IS DISTINCT FROM 'meeting'</c>. It corrects task_count,
    /// unestimated_task_count, earliest_open_due_date and - through the same join - the three
    /// hour buckets. The UPDATE trigger additionally learns to watch <c>task_kind</c>, so
    /// re-kinding a task recomputes its module.
    /// </para>
    /// <para>
    /// This migration is SQL-only: the EF model is untouched, so the model snapshot does not
    /// change. Where the summary table does not exist (a branch with no module-hour summaries at
    /// all) the migration does nothing - there is nothing to correct there. Where the table DOES
    /// exist the correction is always applied, and it fails loudly rather than silently skipping
    /// if the schema it depends on is not what it expects.
    /// </para>
    /// </summary>
    public partial class ExcludeMeetingTasksFromModuleHourSummaries : Migration
    {
        /// <summary>
        /// The refresh function with the meeting predicate. Identical to the body created by
        /// AddModuleHourSummaries except for the added join predicate - the tenant scope
        /// (t.tenant_id = o.tenant_id) and the top-level / not-deleted scope are unchanged.
        /// </summary>
        private const string RefreshFunctionWithMeetingExclusion = """
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
                    AND t.task_kind IS DISTINCT FROM 'meeting'
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
            $fn$
            """;

        /// <summary>
        /// The pre-fix body, restored verbatim by <see cref="Down"/> so a rollback puts the
        /// trigger back exactly where it was.
        /// </summary>
        private const string RefreshFunctionWithoutMeetingExclusion = """
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
            $fn$
            """;

        /// <summary>
        /// PostgreSQL has no ALTER TRIGGER ... WHEN, so the UPDATE trigger is dropped and
        /// recreated with <c>task_kind</c> added to its watched columns. Every other watched
        /// column is the one AddModuleHourSummaries chose: the summary depends on the Module,
        /// the status, the estimate, the progress, the due date, the parent and the soft delete.
        /// </summary>
        private const string UpdateTriggerWatchingTaskKind = """
            CREATE TRIGGER trg_tasks_module_hour_summary_update
                AFTER UPDATE ON tasks
                FOR EACH ROW
                WHEN (OLD.objective_id IS DISTINCT FROM NEW.objective_id
                    OR OLD.status_id IS DISTINCT FROM NEW.status_id
                    OR OLD.estimated_hours IS DISTINCT FROM NEW.estimated_hours
                    OR OLD.progress_percent IS DISTINCT FROM NEW.progress_percent
                    OR OLD.due_date IS DISTINCT FROM NEW.due_date
                    OR OLD.parent_task_id IS DISTINCT FROM NEW.parent_task_id
                    OR OLD.is_deleted IS DISTINCT FROM NEW.is_deleted
                    OR OLD.task_kind IS DISTINCT FROM NEW.task_kind)
                EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary()
            """;

        /// <summary>The pre-fix trigger, restored verbatim by <see cref="Down"/>.</summary>
        private const string UpdateTriggerWithoutTaskKind = """
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
                EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary()
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReplaceRefreshFunction(RefreshFunctionWithMeetingExclusion));
            migrationBuilder.Sql(ReplaceUpdateTrigger(UpdateTriggerWatchingTaskKind));
            migrationBuilder.Sql(BackfillAllModules());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReplaceRefreshFunction(RefreshFunctionWithoutMeetingExclusion));
            migrationBuilder.Sql(ReplaceUpdateTrigger(UpdateTriggerWithoutTaskKind));
            migrationBuilder.Sql(BackfillAllModules());
        }

        /// <summary>
        /// Replaces the refresh function inside one guarded block.
        /// <para>
        /// The guard is on the summary TABLE: where the table exists the correction must be
        /// applied, so the statements run unconditionally and a schema that cannot support them
        /// raises instead of recording itself as applied. Where the table does not exist (a
        /// branch with no module-hour summaries at all) there is nothing to correct and the
        /// block does nothing.
        /// </para>
        /// </summary>
        private static string ReplaceRefreshFunction(string refreshFunction)
        {
            return $$"""
                DO $do$
                BEGIN
                    IF to_regclass('public.wm_module_hour_summaries') IS NULL THEN
                        RETURN;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'tasks' AND column_name = 'task_kind'
                    ) THEN
                        RAISE EXCEPTION
                            'tasks.task_kind is missing - the meeting-task exclusion cannot be applied safely.';
                    END IF;

                    EXECUTE $sql$
                        {{refreshFunction}}
                    $sql$;
                END
                $do$;
                """;
        }

        /// <summary>
        /// Drops and recreates the UPDATE trigger with a wider WHEN clause. PostgreSQL has no
        /// ALTER TRIGGER ... WHEN, so the drop and the create are two separate statements and
        /// therefore two separate EXECUTE calls - PL/pgSQL's EXECUTE runs one statement at a
        /// time. Both sit in the same migration transaction, so a failure between them rolls
        /// both back and leaves the original trigger in place.
        /// </summary>
        private static string ReplaceUpdateTrigger(string createTrigger)
        {
            return $$"""
                DO $do$
                BEGIN
                    IF to_regclass('public.wm_module_hour_summaries') IS NULL THEN
                        RETURN;
                    END IF;

                    IF to_regprocedure('public.wm_tasks_refresh_module_hour_summary()') IS NULL THEN
                        RAISE EXCEPTION
                            'wm_tasks_refresh_module_hour_summary() is missing - '
                            'the meeting-task exclusion cannot be applied safely.';
                    END IF;

                    EXECUTE $sql$ DROP TRIGGER IF EXISTS trg_tasks_module_hour_summary_update ON tasks $sql$;

                    EXECUTE $sql$
                        {{createTrigger}}
                    $sql$;
                END
                $do$;
                """;
        }

        /// <summary>
        /// Recomputes every Module's summary row from its own tasks. Runs in admin mode because
        /// wm_module_hour_summaries has FORCE ROW LEVEL SECURITY - the same shape the
        /// AddModuleHourSummaries backfill used. set_config's third argument makes it local to
        /// this transaction, so the session's tenant mode is untouched.
        /// </summary>
        private static string BackfillAllModules()
        {
            return """
                DO $do$
                BEGIN
                    IF to_regclass('public.wm_module_hour_summaries') IS NULL THEN
                        RETURN;
                    END IF;

                    PERFORM set_config('app.tenant_context_mode', 'admin', true);
                    PERFORM wm_refresh_module_hour_summary(o.id) FROM objectives o;
                END
                $do$;
                """;
        }
    }
}
