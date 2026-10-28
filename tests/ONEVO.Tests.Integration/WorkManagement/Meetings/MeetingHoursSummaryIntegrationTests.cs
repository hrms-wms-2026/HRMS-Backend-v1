using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ONEVO.Infrastructure.Migrations;
using Npgsql;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Meeting hours (2026-10-26 design, Part A): a stamped meeting occurrence's hours count in its module's
/// completed hours, never in the task counts or the open-hour buckets. Runs against the fully migrated schema.</summary>
public sealed class MeetingHoursSummaryIntegrationTests
{
    private sealed record Summary(int TaskCount, int Unestimated, decimal NotStarted, decimal InProgress, decimal Completed);

    private static async Task<Summary> ReadAsync(MyTeamDb h, Guid objectiveId)
    {
        await using var conn = new NpgsqlConnection(h.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT task_count, unestimated_task_count, not_started_hours, in_progress_hours, completed_hours
            FROM wm_module_hour_summaries WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", objectiveId);
        await using var r = await cmd.ExecuteReaderAsync();
        (await r.ReadAsync()).Should().BeTrue("the module has a summary row");
        return new Summary(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4));
    }

    private static async Task<(MyTeamDb H, Guid ObjectiveId)> SeedAsync(decimal? meetingHours)
    {
        var h = await MyTeamDb.CreateAsync();
        Guid objectiveId;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            objectiveId = at.ObjectiveId;
            await db.SaveChangesAsync();   // the module exists before any task points at it (the trigger joins objectives)

            var standard = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0,
                due: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), assigneeEmployeeId: at.OrganizerEmployeeId);
            standard.EstimatedHours = 5m;
            var (meeting, _) = h.AddOccurrence(db, h.AddSeries(db, at), at);
            meeting.EstimatedHours = meetingHours;
            await db.SaveChangesAsync();
        }
        return (h, objectiveId);
    }

    [Fact]
    public async Task Stamped_meeting_adds_completed_hours_but_no_count()
    {
        var (h, objectiveId) = await SeedAsync(meetingHours: 3m);

        var row = await ReadAsync(h, objectiveId);

        row.TaskCount.Should().Be(1, "meetings stay out of the task count");
        row.Unestimated.Should().Be(0);
        row.NotStarted.Should().Be(5m, "a meeting never sits in the open-hour buckets");
        row.InProgress.Should().Be(0m);
        row.Completed.Should().Be(3m, "a stamped meeting's person-hours are completed work");
    }

    [Fact]
    public async Task An_unstamped_meeting_adds_nothing()
    {
        var (h, objectiveId) = await SeedAsync(meetingHours: null);

        var row = await ReadAsync(h, objectiveId);

        row.Should().Be(new Summary(1, 0, 5m, 0m, 0m));
    }

    [Fact]
    public async Task The_reapply_migration_restores_the_trigger_a_misordered_database_lost()
    {
        // A database that ran Plan D before AddModuleHourSummaries existed recorded the fix as a no-op, then got the
        // unfixed trigger (no task_kind watch) from development. The follow-up migration must put it back.
        var (h, _) = await SeedAsync(meetingHours: null);
        await using var conn = new NpgsqlConnection(h.ConnectionString);
        await conn.OpenAsync();
        await using (var drop = new NpgsqlCommand("""
            DROP TRIGGER trg_tasks_module_hour_summary_update ON tasks;
            CREATE TRIGGER trg_tasks_module_hour_summary_update AFTER UPDATE ON tasks FOR EACH ROW
                WHEN (OLD.estimated_hours IS DISTINCT FROM NEW.estimated_hours)
                EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary();
            """, conn))
            await drop.ExecuteNonQueryAsync();

        foreach (var sql in new ReapplyModuleHourSummaryMeetingRules().UpOperations.OfType<SqlOperation>())
        {
            await using var cmd = new NpgsqlCommand(sql.Sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var def = new NpgsqlCommand(
            "SELECT pg_get_triggerdef(t.oid) FROM pg_trigger t WHERE t.tgname = 'trg_tasks_module_hour_summary_update'", conn);
        ((string)(await def.ExecuteScalarAsync())!).Should().Contain("task_kind");
    }
}
