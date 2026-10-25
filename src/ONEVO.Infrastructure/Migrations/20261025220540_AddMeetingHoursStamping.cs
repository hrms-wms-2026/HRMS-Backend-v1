using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Meeting hours (2026-10-26 design, Part A). An ended meeting occurrence is stamped once with its person-hours
    /// (<c>meeting_details.hours_stamped_at</c>, and the task's estimated/completed hours). The module hour summary
    /// counts a meeting row's hours in <c>completed_hours</c> only: meetings stay out of <c>task_count</c>,
    /// <c>unestimated_task_count</c>, <c>earliest_open_due_date</c> and the open-hour buckets, as
    /// <see cref="ExcludeMeetingTasksFromModuleHourSummaries"/> made them. An unstamped meeting has no estimated hours,
    /// so it adds nothing.
    /// </summary>
    public partial class AddMeetingHoursStamping : Migration
    {
        private const string RefreshFunctionWithMeetingHours = """
            CREATE OR REPLACE FUNCTION wm_refresh_module_hour_summary(p_objective_id uuid) RETURNS void
            LANGUAGE sql AS $fn$
                INSERT INTO wm_module_hour_summaries AS s
                    (id, tenant_id, project_id, not_started_hours, in_progress_hours, completed_hours,
                     task_count, unestimated_task_count, earliest_open_due_date,
                     created_at, updated_at, created_by_id, is_deleted)
                SELECT
                    o.id, o.tenant_id, o.project_id,
                    COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.meeting AND NOT x.done AND t.progress_percent <= 0), 0),
                    COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.meeting AND NOT x.done AND t.progress_percent > 0), 0),
                    COALESCE(SUM(t.estimated_hours) FILTER (WHERE x.meeting OR x.done), 0),
                    COUNT(t.id) FILTER (WHERE NOT x.meeting),
                    COUNT(t.id) FILTER (WHERE NOT x.meeting AND COALESCE(t.estimated_hours, 0) = 0),
                    MIN(t.due_date) FILTER (WHERE NOT x.meeting AND NOT x.done),
                    now(), now(), '00000000-0000-0000-0000-000000000000', false
                FROM objectives o
                LEFT JOIN tasks t
                    ON t.objective_id = o.id AND t.tenant_id = o.tenant_id
                    AND t.parent_task_id IS NULL AND NOT t.is_deleted
                LEFT JOIN task_statuses st ON st.id = t.status_id
                CROSS JOIN LATERAL (
                    SELECT (COALESCE(st.marks_task_complete, false) OR COALESCE(t.progress_percent, 0) >= 100) AS done,
                           COALESCE(t.task_kind = 'meeting', false) AS meeting
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hours_stamped_at",
                table: "meeting_details",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.ReplaceRefreshFunction(RefreshFunctionWithMeetingHours));
            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.BackfillAllModules());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.ReplaceRefreshFunction(
                ExcludeMeetingTasksFromModuleHourSummaries.RefreshFunctionWithMeetingExclusion));
            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.BackfillAllModules());

            migrationBuilder.DropColumn(
                name: "hours_stamped_at",
                table: "meeting_details");
        }
    }
}
