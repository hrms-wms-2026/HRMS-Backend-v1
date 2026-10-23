using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
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
    private const string MigrationBeforeFoundation = "20261018080913_AddMeetingProvisioningState";
    private const string Foundation = "20261018092918_AddMeetingWorkItemsFoundation";

    /// <summary>Runs AddMeetingWorkItemsFoundation's own Down (or Up) script against the test's database. Migrating the
    /// database itself below the foundation would also roll back every later migration, and some of those are one-way
    /// (e.g. LeavePolicyPerTypeVersionsAssignments), so these tests exercise only this migration's SQL.</summary>
    private static async Task RunFoundationAsync(MyTeamDb h, bool down, CancellationToken ct = default)
    {
        string script;
        await using (var db = h.NewContext())
        {
            var migrator = db.Database.GetService<IMigrator>();
            script = down ? migrator.GenerateScript(Foundation, MigrationBeforeFoundation)
                : migrator.GenerateScript(MigrationBeforeFoundation, Foundation);
        }
        await using var connection = new NpgsqlConnection(h.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(script, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

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
    public async Task MeetingDetailsUnique_IsNotDeferrable()
    {
        var h = await MyTeamDb.CreateAsync();

        var deferrable = await ScalarAsync(h,
            "SELECT condeferrable FROM pg_constraint WHERE conname = 'uq_meeting_details_series_original_start'");

        Assert.Equal(false, deferrable);
    }

    [Fact]
    public async Task EveryMeetingTable_HasForcedRowLevelSecurity()
    {
        var h = await MyTeamDb.CreateAsync();

        foreach (var table in MeetingTables)
        {
            var forced = await ScalarAsync(h,
                $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = to_regclass('{table}')");
            Assert.True(forced is true, $"{table} must have forced row level security");
        }
    }

    [Fact]
    public async Task OneOwnerChecks_RejectBothOrNeither()
    {
        var h = await MyTeamDb.CreateAsync();

        foreach (var (eventId, seriesId) in new (Guid?, Guid?)[] { (Guid.NewGuid(), Guid.NewGuid()), (null, null) })
        {
            var participant = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(h,
                """
                INSERT INTO calendar_event_participants
                    (id, tenant_id, event_id, meeting_series_id, employee_id, response_status, created_at, created_by_id, is_deleted)
                VALUES (gen_random_uuid(), @tenant, @event, @series, gen_random_uuid(), 'pending', now(), gen_random_uuid(), false)
                """, ("tenant", h.TenantId), ("event", eventId), ("series", seriesId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, participant.SqlState);

            var guest = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(h,
                """
                INSERT INTO calendar_event_guests (id, tenant_id, event_id, meeting_series_id, email, created_at)
                VALUES (gen_random_uuid(), @tenant, @event, @series, 'guest@example.test', now())
                """, ("tenant", h.TenantId), ("event", eventId), ("series", seriesId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, guest.SqlState);
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

        await using var connection = new NpgsqlConnection(a.ConnectionString);
        await connection.OpenAsync();
        await RunAsync(connection, "SET ROLE onevo_app");
        try
        {
            await RunAsync(connection, "SELECT set_config('app.tenant_context_mode', 'tenant', false)");
            await RunAsync(connection, $"SELECT set_config('app.current_tenant_id', '{a.TenantId}', false)");
            await using var select = new NpgsqlCommand("SELECT id FROM meeting_series", connection);
            var visible = new List<Guid>();
            await using (var reader = await select.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    visible.Add(reader.GetGuid(0));

            Assert.Equal(new[] { seriesA }, visible);
        }
        finally
        {
            await RunAsync(connection, "RESET ROLE");
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

        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(a,
            """
            INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, start_at, end_at, is_all_day,
                is_private, is_detached, revision, attendance_checkpoint)
            VALUES (@task, @tenant, @series, now(), now(), now() + interval '30 minutes', false, false, false, 0, 0)
            """, ("task", taskA), ("tenant", a.TenantId), ("series", seriesB)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
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
    public async Task MigrationOverPopulatedCalendarData_Succeeds()
    {
        var h = await MyTeamDb.CreateAsync();
        await using (var db = h.NewContext())
        {
            var organizer = h.AddEmployee(db);
            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Legacy sync", CreatedById = organizer.UserId,
                StartDate = DateTimeOffset.UtcNow.AddDays(1), EndDate = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            };
            db.PersonalCalendarEvents.Add(calendarEvent);
            db.CalendarEventParticipants.Add(new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, EventId = calendarEvent.Id, EmployeeId = organizer.Id,
            });
            db.CalendarEventGuests.Add(new CalendarEventGuest
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, EventId = calendarEvent.Id, Email = "guest@example.test",
            });
            var connection = new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = organizer.UserId, Provider = "outlook_calendar",
                ExternalAccountEmail = "organizer@example.test", CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ExternalCalendarConnections.Add(connection);
            db.CalendarEventMeetings.Add(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, CalendarEventId = calendarEvent.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
                ExternalMeetingId = "teams-1", JoinUrl = "https://teams.example.test/1", CreatedAt = DateTimeOffset.UtcNow,
            });
            db.ExternalCalendarEventLinks.Add(new ExternalCalendarEventLink
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, CalendarEventId = calendarEvent.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = "outlook_calendar",
                ExternalCalendarId = "primary", ExternalEventId = "ext-1", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await RunFoundationAsync(h, down: true);
        await RunFoundationAsync(h, down: false);

        foreach (var check in OwnerChecks)
            Assert.Equal(true, await ScalarAsync(h, $"SELECT convalidated FROM pg_constraint WHERE conname = '{check}'"));
        Assert.Equal(1L, await ScalarAsync(h, "SELECT count(*) FROM calendar_event_meetings WHERE owner_kind = 'calendar_event'"));
    }

    [Fact]
    public async Task Down_RefusesWhileMeetingDataExists()
    {
        var h = await MyTeamDb.CreateAsync();
        await using (var db = h.NewContext())
        {
            h.AddSeries(db, h.AddPlacement(db));
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAnyAsync<Exception>(() => RunFoundationAsync(h, down: true));

        Assert.Contains("meeting data exists", error.ToString());
        Assert.Equal(1L, await ScalarAsync(h, "SELECT count(*) FROM meeting_series"));
    }

    [Fact]
    public async Task PairingTrigger_StillChecks_WhenTheTenantContextChangesBeforeCommit()
    {
        var a = await MyTeamDb.CreateAsync();
        var b = await a.AddTenantAsync();
        MeetingPlacement at;
        await using (var db = a.NewContext())
        {
            at = a.AddPlacement(db);
            await db.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(a.ConnectionString);
        await connection.OpenAsync();
        await RunAsync(connection, "SET ROLE onevo_app");
        try
        {
            await using var tx = await connection.BeginTransactionAsync();
            await UseTenantAsync(connection, a.TenantId);
            await RunAsync(connection, InsertMeetingTaskSql(Guid.NewGuid(), a, at));
            // The commit-time check must not depend on what the session can see by then (spec 4.1 pairing, RLS).
            await UseTenantAsync(connection, b.TenantId);

            var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
            Assert.Contains("trg_meeting_task_has_details", error.MessageText);
        }
        finally
        {
            await RunAsync(connection, "RESET ROLE");
        }
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

        await using var connection = new NpgsqlConnection(h.ConnectionString);
        await connection.OpenAsync();
        await RunAsync(connection, "SET ROLE onevo_app");
        try
        {
            await using var tx = await connection.BeginTransactionAsync();
            await UseTenantAsync(connection, h.TenantId);
            await RunAsync(connection, InsertMeetingTaskSql(taskId, h, at));
            await RunAsync(connection, $"""
                INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, start_at, end_at, is_all_day,
                    is_private, is_detached, revision, attendance_checkpoint)
                VALUES ('{taskId}', '{h.TenantId}', '{seriesId}', '2026-11-02T09:00:00Z', '2026-11-02T09:00:00Z',
                    '2026-11-02T09:30:00Z', false, false, false, 0, 0)
                """);
            await tx.CommitAsync();
        }
        finally
        {
            await RunAsync(connection, "RESET ROLE");
        }

        Assert.Equal(1L, await ScalarAsync(h, $"SELECT count(*) FROM meeting_details WHERE task_id = '{taskId}'"));
    }

    [Fact]
    public async Task Migration_FailsFast_WhenATableItLocksIsBusy()
    {
        var h = await MyTeamDb.CreateAsync();
        await RunFoundationAsync(h, down: true);

        await using var holder = new NpgsqlConnection(h.ConnectionString);
        await holder.OpenAsync();
        await using (var hold = await holder.BeginTransactionAsync())
        {
            await RunAsync(holder, "LOCK TABLE calendar_event_participants IN ACCESS SHARE MODE");
            using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var clock = Stopwatch.StartNew();

            var error = await Assert.ThrowsAnyAsync<Exception>(() => RunFoundationAsync(h, down: false, giveUp.Token));
            Assert.True(InnermostPostgres(error) is { SqlState: PostgresErrorCodes.LockNotAvailable }, error.ToString());
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"the migration queued for {clock.Elapsed} behind the lock");
            await hold.RollbackAsync();
        }

        await RunFoundationAsync(h, down: false);
        Assert.Equal(false, await ScalarAsync(h,
            "SELECT condeferrable FROM pg_constraint WHERE conname = 'uq_meeting_details_series_original_start'"));
    }

    private static PostgresException? InnermostPostgres(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
            if (error is PostgresException postgres)
                return postgres;
        return null;
    }

    private static Task UseTenantAsync(NpgsqlConnection connection, Guid tenantId) => RunAsync(connection,
        $"SELECT set_config('app.tenant_context_mode', 'tenant', true), set_config('app.current_tenant_id', '{tenantId}', true)");

    private static string InsertMeetingTaskSql(Guid taskId, MyTeamDb h, MeetingPlacement at) => $"""
        INSERT INTO tasks (id, tenant_id, task_kind, visibility_scope, project_id, objective_id, short_id, title,
            category_id, status_id, priority, completed_hours, progress_percent, created_at, created_by_id, is_deleted)
        VALUES ('{taskId}', '{h.TenantId}', 'meeting', 'module', '{at.ProjectId}', '{at.ObjectiveId}',
            'M-{taskId:N}', 'Standup', '{h.CategoryIdFor(at.ProjectId)}', '{at.StatusId}', 'medium', 0, 0, now(),
            '{at.OrganizerEmployeeId}', false)
        """;

    private static async Task<object?> ScalarAsync(MyTeamDb h, string sql)
    {
        await using var connection = new NpgsqlConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task ExecuteAsync(MyTeamDb h, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value)
            {
                NpgsqlDbType = value is string ? NpgsqlTypes.NpgsqlDbType.Text : NpgsqlTypes.NpgsqlDbType.Uuid,
            });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
