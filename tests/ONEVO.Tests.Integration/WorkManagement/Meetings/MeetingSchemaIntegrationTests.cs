using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using CalendarEvent = ONEVO.Domain.Features.Calendar.Entities.CalendarEvent;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>The AddMeetingWorkItemsFoundation schema (meetings-as-tasks Phase 2 spec 4.1, 5.6, 8): constraints,
/// deferred pairing/placement triggers, RLS, and a safe up/down over existing calendar data.</summary>
public sealed class MeetingSchemaIntegrationTests
{
    private static readonly string[] MeetingTables =
    [
        "meeting_series", "meeting_details", "meeting_occurrence_suppressions", "meeting_occurrence_attendees",
        "meeting_series_links", "meeting_attendance_sessions", "meeting_action_items",
    ];

    private static readonly string[] OwnerChecks =
    [
        "ck_calendar_event_participants_one_owner", "ck_calendar_event_guests_one_owner",
        "ck_calendar_event_meetings_owner", "ck_external_calendar_event_links_one_owner",
        "ck_meeting_occurrence_attendees_kind",
    ];

    [Fact]
    public async Task EveryMeetingTable_HasForcedRowLevelSecurity()
    {
        var h = await MyTeamDb.CreateAsync();

        foreach (var table in MeetingTables)
        {
            var forced = await ScalarAsync(h,
                $"SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.security_predicates pr JOIN sys.security_policies sp ON sp.object_id = pr.object_id WHERE sp.is_enabled = 1 AND pr.target_object_id = OBJECT_ID('{table}')) THEN 1 ELSE 0 END AS bit)");
            Assert.True(forced is true, $"{table} must have forced row level security");
        }
    }

    [Fact]
    public async Task OneOwnerChecks_RejectBothOrNeither()
    {
        var h = await MyTeamDb.CreateAsync();

        foreach (var (eventId, seriesId) in new (Guid?, Guid?)[] { (Guid.NewGuid(), Guid.NewGuid()), (null, null) })
        {
            var participant = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(h,
                """
                INSERT INTO calendar_event_participants
                    (id, tenant_id, event_id, meeting_series_id, employee_id, response_status, created_at, created_by_id, is_deleted)
                VALUES (NEWID(), @tenant, @event, @series, NEWID(), 'pending', SYSDATETIMEOFFSET(), NEWID(), 0)
                """, ("tenant", h.TenantId), ("event", eventId), ("series", seriesId)));
            Assert.Equal(547, participant.Number);

            var guest = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(h,
                """
                INSERT INTO calendar_event_guests (id, tenant_id, event_id, meeting_series_id, email, created_at)
                VALUES (NEWID(), @tenant, @event, @series, 'guest@example.test', SYSDATETIMEOFFSET())
                """, ("tenant", h.TenantId), ("event", eventId), ("series", seriesId)));
            Assert.Equal(547, guest.Number);
        }
    }

    [Fact]
    public async Task ATenantContextSeesOnlyItsOwnMeetingRows()
    {
        var a = await MyTeamDb.CreateAsync();
        var b = await a.AddTenantAsync();
        Guid seriesA;
        await using (var db = a.NewContext())
        {
            seriesA = a.AddSeries(db, a.AddPlacement(db)).Id;
            b.AddSeries(db, b.AddPlacement(db));
            await db.SaveChangesAsync();
        }

        await using var connection = TestDb.CreateConnection(a.ConnectionString);
        await connection.OpenAsync();
        await RunAsync(connection, "EXECUTE AS USER = 'onevo_app'");
        try
        {
            await RunAsync(connection, "EXEC sp_set_session_context N'app.tenant_context_mode', N'tenant'");
            await RunAsync(connection, $"EXEC sp_set_session_context N'app.current_tenant_id', N'{a.TenantId}'");
            await using var select = new SqlCommand("SELECT id FROM meeting_series", connection);
            var visible = new List<Guid>();
            await using (var reader = await select.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    visible.Add(reader.GetGuid(0));

            Assert.Equal(new[] { seriesA }, visible);
        }
        finally
        {
            await RunAsync(connection, "REVERT");
        }
    }

    [Fact]
    public async Task CompositeFk_RejectsAnotherTenantsSeries()
    {
        var a = await MyTeamDb.CreateAsync();
        var b = await a.AddTenantAsync();
        Guid taskA, seriesB;
        await using (var db = a.NewContext())
        {
            var placementA = a.AddPlacement(db);
            taskA = a.AddTask(db, placementA.ProjectId, placementA.ObjectiveId, placementA.StatusId, progress: 0, due: null).Id;
            seriesB = b.AddSeries(db, b.AddPlacement(db)).Id;
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(a,
            """
            INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, start_at, end_at, is_all_day,
                is_private, is_detached, revision, attendance_checkpoint)
            VALUES (@task, @tenant, @series, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), DATEADD(minute, 30, SYSDATETIMEOFFSET()), 0, 0, 0, 0, 0)
            """, ("task", taskA), ("tenant", a.TenantId), ("series", seriesB)));

        Assert.Equal(547, error.Number);
    }

    [Fact]
    public async Task MeetingTaskWithoutDetails_FailsAtCommit()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        await db.SaveChangesAsync();

        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null);
        task.TaskKind = WorkTaskKinds.Meeting;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        Assert.Contains("trg_meeting_task_has_details", error.ToString());
    }

    [Fact]
    public async Task AMeetingTaskWithItsDetails_Commits()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var series = h.AddSeries(db, at);
        var (task, _) = h.AddOccurrence(db, series, at);

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.MeetingDetails.CountAsync(d => d.TaskId == task.Id));
    }

    [Fact]
    public async Task PlacementMismatch_FailsAtCommit()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var otherModule = h.AddModule(db, at.ProjectId, at.ObjectiveId, at.OrganizerEmployeeId);
        var series = h.AddSeries(db, at);
        await db.SaveChangesAsync();

        var (task, _) = h.AddOccurrence(db, series, at);
        task.ObjectiveId = otherModule;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        Assert.Contains("trg_meeting_placement_matches_series", error.ToString());
    }

    [Fact]
    public async Task AMeetingPairWrittenAsTheAppRoleInTenantMode_Commits()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        Guid seriesId;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            seriesId = h.AddSeries(db, at).Id;
            await db.SaveChangesAsync();
        }
        var taskId = Guid.NewGuid();

        await using var connection = TestDb.CreateConnection(h.ConnectionString);
        await connection.OpenAsync();
        await RunAsync(connection, "EXECUTE AS USER = 'onevo_app'");
        try
        {
            await UseTenantAsync(connection, h.TenantId);
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
            await RunAsync(connection, InsertMeetingTaskSql(taskId, h, at), tx);
            await RunAsync(connection, $"""
                INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, start_at, end_at, is_all_day,
                    is_private, is_detached, revision, attendance_checkpoint)
                VALUES ('{taskId}', '{h.TenantId}', '{seriesId}', '2026-11-02T09:00:00Z', '2026-11-02T09:00:00Z',
                    '2026-11-02T09:30:00Z', false, false, false, 0, 0)
                """, tx);
            await tx.CommitAsync();
        }
        finally
        {
            await RunAsync(connection, "REVERT");
        }

        Assert.Equal(1L, await ScalarAsync(h, $"SELECT COUNT_BIG(*) FROM meeting_details WHERE task_id = '{taskId}'"));
    }

    private static Task UseTenantAsync(SqlConnection connection, Guid tenantId) => RunAsync(connection,
        $"EXEC sp_set_session_context N'app.tenant_context_mode', N'tenant'; EXEC sp_set_session_context N'app.current_tenant_id', N'{tenantId}'");

    private static string InsertMeetingTaskSql(Guid taskId, MyTeamDb h, MeetingPlacement at) => $"""
        INSERT INTO tasks (id, tenant_id, task_kind, visibility_scope, project_id, objective_id, short_id, title,
            category_id, status_id, priority, completed_hours, progress_percent, created_at, created_by_id, is_deleted)
        VALUES ('{taskId}', '{h.TenantId}', 'meeting', 'module', '{at.ProjectId}', '{at.ObjectiveId}',
            'M-{taskId:N}', 'Standup', '{h.CategoryIdFor(at.ProjectId)}', '{at.StatusId}', 'medium', 0, 0, SYSDATETIMEOFFSET(),
            '{at.OrganizerEmployeeId}', 0)
        """;

    private static async Task<object?> ScalarAsync(MyTeamDb h, string sql)
    {
        await using var connection = TestDb.CreateConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task ExecuteAsync(MyTeamDb h, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = TestDb.CreateConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new SqlParameter(name, value ?? DBNull.Value));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RunAsync(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
