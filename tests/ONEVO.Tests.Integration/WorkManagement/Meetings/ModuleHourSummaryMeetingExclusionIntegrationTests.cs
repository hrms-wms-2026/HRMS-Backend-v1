using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// Pins that meeting occurrences never reach the Team Progress module-hour summaries.
/// <para>
/// A meeting occurrence is an ordinary row in <c>tasks</c> (task_kind = 'meeting'). EF Core hides
/// those rows from every ordinary task query through the named <c>ExcludeMeetings</c> filter, but
/// <c>wm_refresh_module_hour_summary</c> is raw SQL and never sees that filter - its join on
/// <c>tasks</c> had no <c>task_kind</c> predicate, so every meeting occurrence inflated
/// <c>task_count</c> and <c>unestimated_task_count</c> (a meeting occurrence carries no
/// estimated_hours, so it also landed in the "unestimated" bucket and moved
/// <c>earliest_open_due_date</c>).
/// </para>
/// <para>
/// The migration that fixes it is SQL-only and must be safe on a branch that has no module-hour
/// summaries at all, so these tests do not assume the summary table exists. Each one installs the
/// pre-fix schema (the same DDL AddModuleHourSummaries creates) into its own disposable database,
/// seeds real rows, runs the migration's own SQL, and asserts on the resulting rows. The SQL
/// under test is read out of the migration's UpOperations, so the fixture never duplicates it.
/// </para>
/// </summary>
public sealed class ModuleHourSummaryMeetingExclusionIntegrationTests
{
    private static readonly Migration MigrationUnderTest = new ExcludeMeetingTasksFromModuleHourSummaries();

    /// <summary>The DDL AddModuleHourSummaries creates - the pre-fix state this migration repairs.</summary>
    private const string PreFixSchema = """
        CREATE TABLE wm_module_hour_summaries (
            id uuid NOT NULL,
            project_id uuid NOT NULL,
            not_started_hours numeric(18,2) NOT NULL,
            in_progress_hours numeric(18,2) NOT NULL,
            completed_hours numeric(18,2) NOT NULL,
            task_count integer NOT NULL,
            unestimated_task_count integer NOT NULL,
            earliest_open_due_date date NULL,
            tenant_id uuid NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NULL,
            created_by_id uuid NOT NULL,
            is_deleted boolean NOT NULL,
            deleted_at timestamptz NULL,
            CONSTRAINT pk_wm_module_hour_summaries PRIMARY KEY (id)
        );

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
        """;

    /// <summary>Installs the pre-fix schema into the test's own disposable database.</summary>
    private static async Task InstallPreFixSchemaAsync(MyTeamDb helper)
    {
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = PreFixSchema;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Runs the migration's own Up (or Down) SQL against the connection - the fixture
    /// never duplicates the SQL under test.</summary>
    private static async Task ApplyMigrationAsync(NpgsqlConnection conn, bool down = false)
    {
        var operations = down ? MigrationUnderTest.DownOperations : MigrationUnderTest.UpOperations;
        foreach (var sql in operations.OfType<SqlOperation>())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql.Sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private sealed record SummaryRow(int TaskCount, int Unestimated, decimal NotStarted, DateOnly? Earliest);

    private static async Task<SummaryRow?> ReadSummaryAsync(NpgsqlConnection conn, Guid objectiveId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT task_count, unestimated_task_count, not_started_hours, earliest_open_due_date
            FROM wm_module_hour_summaries WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", objectiveId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new SummaryRow(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)));
    }

    private static async Task RefreshAsync(NpgsqlConnection conn, Guid objectiveId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT wm_refresh_module_hour_summary(@o)";
        cmd.Parameters.AddWithValue("o", objectiveId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// One module with one standard task and (optionally) one meeting occurrence.
    /// <para>
    /// The Module and its status are committed before the tasks: tasks has no foreign key to
    /// objectives, so a single SaveChanges can insert a task before the Module it points at, and
    /// the trigger - which joins objectives - would then find no Module to summarize. Production
    /// never hits this (a Module always pre-exists); the fixture has to reproduce that order.
    /// </para>
    /// </summary>
    private static async Task<(MyTeamDb Db, Guid ObjectiveId, Guid StandardTaskId, Guid MeetingTaskId)> SeedAsync(
        bool withMeeting = true, DateOnly? meetingDueDate = null, decimal standardHours = 5m)
    {
        var helper = await MyTeamDb.CreateAsync();
        await InstallPreFixSchemaAsync(helper);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid objectiveId, standardTaskId = Guid.Empty, meetingTaskId = Guid.Empty;

        await using (var db = helper.NewContext())
        {
            var at = helper.AddPlacement(db);
            objectiveId = at.ObjectiveId;
            await db.SaveChangesAsync();   // the Module exists before any task points at it

            var standard = helper.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0,
                due: today.AddDays(3), assigneeEmployeeId: at.OrganizerEmployeeId);
            standard.EstimatedHours = standardHours;
            standardTaskId = standard.Id;

            if (withMeeting)
            {
                var (meeting, _) = helper.AddOccurrence(db, helper.AddSeries(db, at), at);
                if (meetingDueDate is { } due)
                    meeting.DueDate = due;
                meetingTaskId = meeting.Id;
            }

            await db.SaveChangesAsync();
        }

        return (helper, objectiveId, standardTaskId, meetingTaskId);
    }

    [Fact]
    public async Task BeforeTheFix_TheMeetingOccurrence_InflatesTheCounts()
    {
        // The defect itself: with the pre-fix function, the meeting occurrence is counted.
        var (helper, objectiveId, _, _) = await SeedAsync();
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();

        await RefreshAsync(conn, objectiveId);
        var row = await ReadSummaryAsync(conn, objectiveId);

        row.Should().NotBeNull();
        row!.TaskCount.Should().Be(2, "the pre-fix trigger has no task_kind predicate");
        row.Unestimated.Should().Be(1, "a meeting occurrence carries no estimated_hours");
    }

    [Fact]
    public async Task AfterTheFix_TheMeetingOccurrence_IsNotCounted()
    {
        var (helper, objectiveId, _, _) = await SeedAsync();
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();

        await ApplyMigrationAsync(conn);
        await RefreshAsync(conn, objectiveId);
        var row = await ReadSummaryAsync(conn, objectiveId);

        row.Should().NotBeNull();
        row!.TaskCount.Should().Be(1, "only the standard task may be counted");
        row.Unestimated.Should().Be(0, "the meeting occurrence must not count as unestimated");
        row.NotStarted.Should().Be(5m, "the hours come from the standard task only");
    }

    [Fact]
    public async Task AfterTheFix_AModuleWithOnlyAMeetingOccurrence_ReportsNoTasks()
    {
        var (helper, objectiveId, _, _) = await SeedAsync();
        // Remove the standard task so the module holds only the meeting occurrence.
        await using (var conn = new NpgsqlConnection(helper.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM tasks WHERE objective_id = @o AND task_kind = 'standard'";
            cmd.Parameters.AddWithValue("o", objectiveId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var conn = new NpgsqlConnection(helper.ConnectionString))
        {
            await conn.OpenAsync();
            await ApplyMigrationAsync(conn);
            await RefreshAsync(conn, objectiveId);
            var row = await ReadSummaryAsync(conn, objectiveId);

            row.Should().NotBeNull();
            row!.TaskCount.Should().Be(0, "a module with only a meeting occurrence has no work in it");
            row.Unestimated.Should().Be(0);
        }
    }

    [Fact]
    public async Task AfterTheFix_AMeetingOccurrenceWithADueDate_DoesNotMoveTheEarliestDueDate()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (helper, objectiveId, _, _) = await SeedAsync(meetingDueDate: today.AddDays(-2));
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();

        await ApplyMigrationAsync(conn);
        await RefreshAsync(conn, objectiveId);
        var row = await ReadSummaryAsync(conn, objectiveId);

        row.Should().NotBeNull();
        row!.Earliest.Should().Be(today.AddDays(3), "the meeting occurrence's due date must not count");
    }

    [Fact]
    public async Task AfterTheFix_CancellingTheMeeting_LeavesTheCountsUnchanged()
    {
        // Cancelling a meeting sets meeting_details.cancelled_at; the tasks row stays, so the
        // meeting must be excluded from the counts both before and after the cancellation.
        var (helper, objectiveId, _, meetingTaskId) = await SeedAsync();
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();

        await ApplyMigrationAsync(conn);
        await RefreshAsync(conn, objectiveId);
        var before = await ReadSummaryAsync(conn, objectiveId);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE meeting_details SET cancelled_at = now() WHERE task_id = @t";
            cmd.Parameters.AddWithValue("t", meetingTaskId);
            await cmd.ExecuteNonQueryAsync();
        }

        await RefreshAsync(conn, objectiveId);
        var after = await ReadSummaryAsync(conn, objectiveId);

        before.Should().NotBeNull();
        after.Should().NotBeNull();
        before!.TaskCount.Should().Be(1);
        after!.TaskCount.Should().Be(1, "a cancelled meeting occurrence is still excluded, not counted");
        after.Unestimated.Should().Be(0);
    }

    [Fact]
    public async Task AfterTheFix_OneTenantIsUnaffectedByAnotherTenantsMeetingOccurrences()
    {
        var helper = await MyTeamDb.CreateAsync();
        await InstallPreFixSchemaAsync(helper);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid objectiveA, objectiveB;

        await using (var db = helper.NewContext())
        {
            var at = helper.AddPlacement(db);
            objectiveA = at.ObjectiveId;
            await db.SaveChangesAsync();
            var standard = helper.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0,
                due: today.AddDays(3), assigneeEmployeeId: at.OrganizerEmployeeId);
            standard.EstimatedHours = 5m;
            helper.AddOccurrence(db, helper.AddSeries(db, at), at);
            await db.SaveChangesAsync();
        }

        var other = await helper.AddTenantAsync();
        await using (var db = other.NewContext())
        {
            var at = other.AddPlacement(db);
            objectiveB = at.ObjectiveId;
            await db.SaveChangesAsync();
            var standard = other.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0,
                due: today.AddDays(3), assigneeEmployeeId: at.OrganizerEmployeeId);
            standard.EstimatedHours = 7m;
            await db.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();
        await ApplyMigrationAsync(conn);
        await RefreshAsync(conn, objectiveA);
        await RefreshAsync(conn, objectiveB);

        var rowA = await ReadSummaryAsync(conn, objectiveA);
        var rowB = await ReadSummaryAsync(conn, objectiveB);

        rowA.Should().NotBeNull();
        rowB.Should().NotBeNull();
        rowA!.TaskCount.Should().Be(1, "the meeting occurrence must not be counted");
        rowA.Unestimated.Should().Be(0);
        rowB!.TaskCount.Should().Be(1, "the other tenant's module holds one standard task");
        rowB.Unestimated.Should().Be(0);
        rowB.NotStarted.Should().Be(7m);
    }

    [Fact]
    public async Task AfterTheFix_ChangingATaskKind_RecomputesTheSummary()
    {
        // The UPDATE trigger must watch task_kind, so re-kinding a task refreshes its module.
        // A meeting task and its meeting_details row are 1:1 (the database enforces it), so the
        // two writes happen in one transaction: the occurrence stops being a meeting and stops
        // being an occurrence at the same time.
        var (helper, objectiveId, _, meetingTaskId) = await SeedAsync();

        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();
        await ApplyMigrationAsync(conn);

        // Leave the module holding only the meeting occurrence.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM tasks WHERE objective_id = @o AND task_kind = 'standard'";
            cmd.Parameters.AddWithValue("o", objectiveId);
            await cmd.ExecuteNonQueryAsync();
        }
        await RefreshAsync(conn, objectiveId);
        (await ReadSummaryAsync(conn, objectiveId))!.TaskCount
            .Should().Be(0, "a module holding only a meeting occurrence has no work in it");

        // The occurrence stops being a meeting. The summary must pick the task up.
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM meeting_details WHERE task_id = @t";
                cmd.Parameters.AddWithValue("t", meetingTaskId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE tasks SET task_kind = 'standard' WHERE id = @t";
                cmd.Parameters.AddWithValue("t", meetingTaskId);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        (await ReadSummaryAsync(conn, objectiveId))!.TaskCount
            .Should().Be(1, "re-kinding a task must recompute the summary");
    }

    [Fact]
    public async Task TheLiveFunction_ExcludesMeetings_AndTheUpdateTriggerWatchesTaskKind()
    {
        var (helper, _, _, _) = await SeedAsync();
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();
        await ApplyMigrationAsync(conn);

        string functionDef;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE p.proname = 'wm_refresh_module_hour_summary'
                """;
            functionDef = (string)(await cmd.ExecuteScalarAsync())!;
        }

        functionDef.Should().Contain("task_kind IS DISTINCT FROM 'meeting'");

        string triggerDef;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT pg_get_triggerdef(t.oid) FROM pg_trigger t WHERE t.tgname = 'trg_tasks_module_hour_summary_update'";
            triggerDef = (string)(await cmd.ExecuteScalarAsync())!;
        }

        triggerDef.Should().Contain("task_kind");
    }

    [Fact]
    public async Task TheBackfill_RecomputesExistingRows()
    {
        var (helper, objectiveId, _, _) = await SeedAsync();

        // Inflate the row the way the pre-fix trigger did.
        await using (var conn = new NpgsqlConnection(helper.ConnectionString))
        {
            await conn.OpenAsync();
            await RefreshAsync(conn, objectiveId);
            (await ReadSummaryAsync(conn, objectiveId))!.TaskCount.Should().Be(2);
        }

        // The migration's own backfill (not a manual per-module refresh) must correct it.
        await using (var conn = new NpgsqlConnection(helper.ConnectionString))
        {
            await conn.OpenAsync();
            await ApplyMigrationAsync(conn);
            var row = await ReadSummaryAsync(conn, objectiveId);

            row.Should().NotBeNull();
            row!.TaskCount.Should().Be(1, "the backfill must correct the inflated count");
            row.Unestimated.Should().Be(0);
        }
    }

    [Fact]
    public async Task TheDownMigration_RestoresThePreFixBehaviour_AndCanBeReapplied()
    {
        var (helper, objectiveId, _, _) = await SeedAsync();
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();

        await ApplyMigrationAsync(conn);
        await RefreshAsync(conn, objectiveId);
        (await ReadSummaryAsync(conn, objectiveId))!.TaskCount.Should().Be(1);

        // Roll back: the pre-fix body is restored and the backfill runs again.
        await ApplyMigrationAsync(conn, down: true);
        await RefreshAsync(conn, objectiveId);
        (await ReadSummaryAsync(conn, objectiveId))!.TaskCount
            .Should().Be(2, "the rollback restores the pre-fix trigger body");

        // And the fix can be applied again on top of the rolled-back state.
        await ApplyMigrationAsync(conn);
        await RefreshAsync(conn, objectiveId);
        (await ReadSummaryAsync(conn, objectiveId))!.TaskCount.Should().Be(1);
    }

    [Fact]
    public async Task TheMigration_IsANoOp_WhereThereAreNoModuleHourSummaries()
    {
        // A branch without the summary table must still be able to apply the migration.
        var helper = await MyTeamDb.CreateAsync();
        await using var conn = new NpgsqlConnection(helper.ConnectionString);
        await conn.OpenAsync();

        await ApplyMigrationAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pg_class WHERE relname = 'wm_module_hour_summaries'";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(0, "the migration must not create the summary table on a branch that has none");
    }
}
