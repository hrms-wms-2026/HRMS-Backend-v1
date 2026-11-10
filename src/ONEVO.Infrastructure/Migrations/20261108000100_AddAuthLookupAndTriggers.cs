using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// The pre-tenant login lookup and the database triggers. Grants on these objects are in
    /// ops/sqlserver/post-migration-grants.sql, because onevo_migrator cannot grant them.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108000100_AddAuthLookupAndTriggers")]
    public class AddAuthLookupAndTriggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA auth_internal;");

            // Sole pre-tenant candidate lookup path. It runs as a user with no login, which may read
            // only these columns, and which the users read predicate allows. No dynamic SQL.
            migrationBuilder.Sql("""
                CREATE PROCEDURE auth_internal.auth_lookup_base_login_candidates
                    @normalized_email nvarchar(255)
                WITH EXECUTE AS N'onevo_auth_base_login_fn_owner'
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT TOP (9) u.tenant_id, u.id AS user_id, t.slug, t.name AS display_name, u.password_hash
                    FROM dbo.users u
                    JOIN dbo.tenants t ON t.id = u.tenant_id
                    WHERE u.normalized_email = @normalized_email
                      AND u.is_active = 1
                      AND u.is_deleted = 0
                      AND t.status IN (N'Active', N'Trial')
                    ORDER BY u.tenant_id, u.id;
                END;
                """);

            // Runs under the caller's tenant context, so it only ever touches that tenant's rows.
            // @objective_ids is a JSON array of { "id": ... } objects.
            migrationBuilder.Sql("""
                CREATE PROCEDURE dbo.wm_refresh_module_hour_summary
                    @objective_ids nvarchar(max)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @summaries TABLE (
                        id uniqueidentifier PRIMARY KEY,
                        tenant_id uniqueidentifier NOT NULL,
                        project_id uniqueidentifier NOT NULL,
                        not_started_hours decimal(18,2) NOT NULL,
                        in_progress_hours decimal(18,2) NOT NULL,
                        completed_hours decimal(18,2) NOT NULL,
                        task_count int NOT NULL,
                        unestimated_task_count int NOT NULL,
                        earliest_open_due_date date NULL);

                    INSERT INTO @summaries
                    SELECT
                        o.id, o.tenant_id, o.project_id,
                        COALESCE(SUM(CASE WHEN x.meeting = 0 AND x.done = 0 AND t.progress_percent <= 0 THEN t.estimated_hours END), 0),
                        COALESCE(SUM(CASE WHEN x.meeting = 0 AND x.done = 0 AND t.progress_percent > 0 THEN t.estimated_hours END), 0),
                        COALESCE(SUM(CASE WHEN x.meeting = 1 OR x.done = 1 THEN t.estimated_hours END), 0),
                        COUNT(CASE WHEN x.meeting = 0 THEN t.id END),
                        COUNT(CASE WHEN x.meeting = 0 AND COALESCE(t.estimated_hours, 0) = 0 THEN t.id END),
                        MIN(CASE WHEN x.meeting = 0 AND x.done = 0 THEN t.due_date END)
                    FROM dbo.objectives o
                    JOIN OPENJSON(@objective_ids) WITH (id uniqueidentifier '$.id') ids ON ids.id = o.id
                    LEFT JOIN dbo.tasks t
                        ON t.objective_id = o.id AND t.tenant_id = o.tenant_id
                        AND t.parent_task_id IS NULL AND t.is_deleted = 0
                    LEFT JOIN dbo.task_statuses st ON st.id = t.status_id
                    CROSS APPLY (
                        SELECT
                            CASE WHEN COALESCE(st.marks_task_complete, 0) = 1
                                   OR COALESCE(t.progress_percent, 0) >= 100 THEN 1 ELSE 0 END AS done,
                            CASE WHEN t.task_kind = N'meeting' THEN 1 ELSE 0 END AS meeting
                    ) x
                    GROUP BY o.id, o.tenant_id, o.project_id;

                    UPDATE s
                    SET not_started_hours = n.not_started_hours,
                        in_progress_hours = n.in_progress_hours,
                        completed_hours = n.completed_hours,
                        task_count = n.task_count,
                        unestimated_task_count = n.unestimated_task_count,
                        earliest_open_due_date = n.earliest_open_due_date,
                        updated_at = SYSDATETIMEOFFSET()
                    FROM dbo.wm_module_hour_summaries s WITH (UPDLOCK, HOLDLOCK)
                    JOIN @summaries n ON n.id = s.id;

                    INSERT INTO dbo.wm_module_hour_summaries
                        (id, tenant_id, project_id, not_started_hours, in_progress_hours, completed_hours,
                         task_count, unestimated_task_count, earliest_open_due_date,
                         created_at, updated_at, created_by_id, is_deleted)
                    SELECT
                        n.id, n.tenant_id, n.project_id, n.not_started_hours, n.in_progress_hours, n.completed_hours,
                        n.task_count, n.unestimated_task_count, n.earliest_open_due_date,
                        SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), '00000000-0000-0000-0000-000000000000', 0
                    FROM @summaries n
                    WHERE NOT EXISTS (
                        SELECT 1 FROM dbo.wm_module_hour_summaries s WITH (UPDLOCK, HOLDLOCK) WHERE s.id = n.id);
                END;
                """);

            // Only the columns the summary depends on; title and description edits never recompute.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_tasks_module_hour_summary ON dbo.tasks
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    IF @@ROWCOUNT = 0 RETURN;
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
                       AND NOT (UPDATE(objective_id) OR UPDATE(status_id) OR UPDATE(estimated_hours)
                            OR UPDATE(progress_percent) OR UPDATE(due_date) OR UPDATE(parent_task_id)
                            OR UPDATE(is_deleted) OR UPDATE(task_kind))
                        RETURN;

                    DECLARE @ids nvarchar(max) = (
                        SELECT objective_id AS id
                        FROM (SELECT objective_id FROM inserted UNION SELECT objective_id FROM deleted) changed
                        WHERE objective_id IS NOT NULL
                        FOR JSON PATH);
                    IF @ids IS NOT NULL EXEC dbo.wm_refresh_module_hour_summary @objective_ids = @ids;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_task_statuses_module_hour_summary ON dbo.task_statuses
                AFTER UPDATE
                AS
                BEGIN
                    IF @@ROWCOUNT = 0 RETURN;
                    SET NOCOUNT ON;
                    IF NOT UPDATE(marks_task_complete) RETURN;

                    DECLARE @ids nvarchar(max) = (
                        SELECT DISTINCT t.objective_id AS id
                        FROM inserted i
                        JOIN deleted d ON d.id = i.id AND d.marks_task_complete <> i.marks_task_complete
                        JOIN dbo.tasks t ON t.status_id = i.id
                        WHERE t.objective_id IS NOT NULL
                        FOR JSON PATH);
                    IF @ids IS NOT NULL EXEC dbo.wm_refresh_module_hour_summary @objective_ids = @ids;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_objectives_module_hour_summary_insert ON dbo.objectives
                AFTER INSERT
                AS
                BEGIN
                    IF @@ROWCOUNT = 0 RETURN;
                    SET NOCOUNT ON;
                    DECLARE @ids nvarchar(max) = (SELECT id FROM inserted FOR JSON PATH);
                    EXEC dbo.wm_refresh_module_hour_summary @objective_ids = @ids;
                END;
                """);

            // An occurrence's key never changes inside its series. A remap into another series may
            // change original_start; the task id never changes.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_meeting_details_key_immutable ON dbo.meeting_details
                AFTER UPDATE
                AS
                BEGIN
                    IF @@ROWCOUNT = 0 RETURN;
                    SET NOCOUNT ON;
                    IF UPDATE(task_id) AND EXISTS (
                        SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.task_id = i.task_id))
                        THROW 50000, N'trg_meeting_details_key_immutable: an occurrence cannot change its task id.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        JOIN deleted d ON d.task_id = i.task_id
                        WHERE i.series_id = d.series_id AND i.original_start <> d.original_start)
                        THROW 50000, N'trg_meeting_details_key_immutable: an occurrence cannot change its key within its series.', 1;
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("The SQL Server baseline has no earlier state to return to.");
    }
}
