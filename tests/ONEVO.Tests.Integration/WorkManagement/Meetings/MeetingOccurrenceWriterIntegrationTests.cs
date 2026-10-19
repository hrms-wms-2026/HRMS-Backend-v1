using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Meetings-as-tasks Phase 2 spec 5.6, verified on PostgreSQL: the occurrence insert is idempotent and
/// concurrency-safe without relying on the series lock, suppressions block it, and a split remap keeps task ids.</summary>
public sealed class MeetingOccurrenceWriterIntegrationTests
{
    private static readonly DateTimeOffset FirstStart = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(MyTeamDb H, MeetingPlacement At, MeetingSeries Series);

    private static async Task<Fixture> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var series = h.AddSeries(db, at, FirstStart, "FREQ=WEEKLY");
        await db.SaveChangesAsync();
        return new Fixture(h, at, series);
    }

    private static MeetingOccurrenceDraft Draft(Fixture f, DateTimeOffset originalStart, string? shortId = null, Guid? seriesId = null)
        => new(f.H.TenantId, seriesId ?? f.Series.Id, f.Series.ProjectId, f.Series.ObjectiveId,
            f.H.CategoryIdFor(f.Series.ProjectId), f.At.StatusId,
            shortId ?? $"M-{Guid.NewGuid():N}"[..12], f.Series.Title, "Agenda", f.At.OrganizerEmployeeId,
            originalStart, originalStart, originalStart.AddMinutes(30), IsAllDay: false,
            Location: null, IsPrivate: false, CalendarEventMeetingId: null);

    /// <summary>One writer in its own transaction; returns the id, or rethrows (commit included). <paramref name="gate"/>
    /// releases every racer at once and <paramref name="holdBeforeCommit"/> keeps the winner uncommitted for a moment,
    /// so the racers genuinely overlap instead of running one after another.</summary>
    private static async Task<Guid?> MaterializeInTransactionAsync(Fixture f, MeetingOccurrenceDraft draft, bool takeLock,
        StartGate? gate = null, TimeSpan? holdBeforeCommit = null)
    {
        await using var db = f.H.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var writer = new EfMeetingOccurrenceWriter(db);
        if (gate is not null)
            await gate.ArriveAndWaitAsync();
        if (takeLock)
            await writer.LockSeriesAsync(draft.TenantId, draft.SeriesId, CancellationToken.None);
        var id = await writer.TryMaterializeAsync(draft, CancellationToken.None);
        if (holdBeforeCommit is { } hold)
            await Task.Delay(hold);
        await tx.CommitAsync();
        return id;
    }

    private sealed class StartGate(int racers)
    {
        private readonly TaskCompletionSource _go = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == racers)
                _go.SetResult();
            return _go.Task;
        }
    }

    private static async Task<long> CountAsync(Fixture f, string sql)
    {
        await using var connection = new NpgsqlConnection(f.H.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> OrphanMeetingTasksAsync(Fixture f) => CountAsync(f,
        "SELECT count(*) FROM tasks t WHERE t.task_kind = 'meeting' AND NOT EXISTS (SELECT 1 FROM meeting_details d WHERE d.task_id = t.id)");

    private static Task<long> DetailsAtAsync(Fixture f, Guid seriesId, DateTimeOffset originalStart) => CountAsync(f,
        $"SELECT count(*) FROM meeting_details WHERE series_id = '{seriesId}' AND original_start = '{originalStart.UtcDateTime:O}'::timestamptz");

    [Fact]
    public async Task RacingForASlotWhoseDeterministicIdIsHeld_LosersGetARetryableSignal_NoOrphan()
    {
        var f = await SeedAsync();
        var moved = (await MaterializeInTransactionAsync(f, Draft(f, FirstStart), takeLock: true))!.Value;
        MeetingSeries target;
        await using (var db = f.H.NewContext())
        {
            target = f.H.AddSeries(db, f.At, FirstStart.AddHours(1));
            await db.SaveChangesAsync();
        }
        await using (var db = f.H.NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await new EfMeetingOccurrenceWriter(db).RemapIntoSeriesAsync(f.H.TenantId, target.Id,
                [(moved, FirstStart.AddHours(1), FirstStart.AddHours(1), FirstStart.AddHours(1).AddMinutes(30))], CancellationToken.None);
            await tx.CommitAsync();
        }

        // The old slot's deterministic id is now held by the moved task, so every racer inserts a task with a fresh id
        // and all but one lose the details race (spec 5.6).
        var raceLost = 0;
        var gate = new StartGate(16);
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
        {
            try
            {
                return await MaterializeInTransactionAsync(f, Draft(f, FirstStart, $"M-held-{i}"), takeLock: false,
                    gate, holdBeforeCommit: TimeSpan.FromMilliseconds(300));
            }
            catch (MeetingOccurrenceRaceLostException)
            {
                Interlocked.Increment(ref raceLost);
                return await MaterializeInTransactionAsync(f, Draft(f, FirstStart, $"M-held-retry-{i}"), takeLock: false);
            }
        })));

        Assert.Single(results, r => r is not null);
        Assert.Equal(1, await DetailsAtAsync(f, f.Series.Id, FirstStart));
        Assert.Equal(0, await OrphanMeetingTasksAsync(f));
        Assert.True(raceLost > 0, "no writer lost the race - the test did not exercise the fresh-id path");
    }

    [Fact]
    public async Task Writes_OutsideATransaction_AreRefused()
    {
        var f = await SeedAsync();
        await using var db = f.H.NewContext();
        var writer = new EfMeetingOccurrenceWriter(db);

        var materialize = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.TryMaterializeAsync(Draft(f, FirstStart), CancellationToken.None));
        var remap = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.RemapIntoSeriesAsync(
            f.H.TenantId, f.Series.Id, [(Guid.NewGuid(), FirstStart, FirstStart, FirstStart.AddMinutes(30))], CancellationToken.None));

        Assert.Contains("transaction", materialize.Message);
        Assert.Contains("transaction", remap.Message);
        Assert.Equal(0, await DetailsAtAsync(f, f.Series.Id, FirstStart));
    }

    [Fact]
    public async Task Constraint_IsNotDeferrable()
    {
        var f = await SeedAsync();
        await using var connection = new NpgsqlConnection(f.H.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT condeferrable FROM pg_constraint WHERE conname = 'uq_meeting_details_series_original_start'", connection);

        Assert.Equal(false, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Materialize_Twice_InsertsOnce()
    {
        var f = await SeedAsync();

        var first = await MaterializeInTransactionAsync(f, Draft(f, FirstStart), takeLock: true);
        var second = await MaterializeInTransactionAsync(f, Draft(f, FirstStart), takeLock: true);

        Assert.Equal(MeetingOccurrenceIds.For(f.Series.Id, FirstStart), first);
        Assert.Null(second);
        Assert.Equal(1, await DetailsAtAsync(f, f.Series.Id, FirstStart));
        Assert.Equal(1, await CountAsync(f, "SELECT count(*) FROM tasks WHERE task_kind = 'meeting'"));
    }

    [Fact]
    public async Task SixteenParallelWriters_WithoutTheLock_OneRow_NoOrphanTask()
    {
        var f = await SeedAsync();

        var writers = Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
        {
            try
            {
                return await MaterializeInTransactionAsync(f, Draft(f, FirstStart, $"M-race-{i}"), takeLock: false);
            }
            catch (Exception ex) when (ex.ToString().Contains("trg_meeting_task_has_details"))
            {
                // spec 5.6: a writer that lost the details race fails at commit and the idempotent generator retries.
                return await MaterializeInTransactionAsync(f, Draft(f, FirstStart, $"M-retry-{i}"), takeLock: false);
            }
        })).ToList();
        var results = await Task.WhenAll(writers);

        Assert.Single(results, r => r is not null);
        Assert.Equal(1, await DetailsAtAsync(f, f.Series.Id, FirstStart));
        Assert.Equal(0, await OrphanMeetingTasksAsync(f));
    }

    [Fact]
    public async Task SixteenParallelWriters_WithTheLock_NoAborts()
    {
        var f = await SeedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(
            () => MaterializeInTransactionAsync(f, Draft(f, FirstStart, $"M-lock-{i}"), takeLock: true))));

        Assert.Single(results, r => r is not null);
        Assert.Equal(1, await DetailsAtAsync(f, f.Series.Id, FirstStart));
        Assert.Equal(0, await OrphanMeetingTasksAsync(f));
    }

    [Fact]
    public async Task Remap_WithATimeShift_KeepsTaskIds()
    {
        var f = await SeedAsync();
        var starts = Enumerable.Range(0, 7).Select(w => FirstStart.AddDays(7 * w)).ToList();
        var taskIds = new List<Guid>();
        foreach (var start in starts)
            taskIds.Add((await MaterializeInTransactionAsync(f, Draft(f, start), takeLock: true))!.Value);
        MeetingSeries target;
        await using (var db = f.H.NewContext())
        {
            target = f.H.AddSeries(db, f.At, FirstStart.AddHours(1), "FREQ=WEEKLY");
            await db.SaveChangesAsync();
        }

        await using (var db = f.H.NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await new EfMeetingOccurrenceWriter(db).RemapIntoSeriesAsync(f.H.TenantId, target.Id,
                taskIds.Select((id, i) => (id, starts[i].AddHours(1), starts[i].AddHours(1), starts[i].AddHours(1).AddMinutes(30))).ToList(),
                CancellationToken.None);
            await tx.CommitAsync();
        }

        await using (var db = f.H.NewContext())
        {
            var moved = await db.MeetingDetails.Where(d => taskIds.Contains(d.TaskId)).ToListAsync();
            Assert.Equal(7, moved.Count);
            Assert.All(moved, d => Assert.Equal(target.Id, d.SeriesId));
            Assert.Equal(starts.Select(s => s.AddHours(1)), moved.OrderBy(d => d.OriginalStart).Select(d => d.OriginalStart));
            Assert.False(await db.MeetingDetails.AnyAsync(d => d.SeriesId == f.Series.Id));
        }
        Assert.NotNull(await MaterializeInTransactionAsync(f, Draft(f, starts[0]), takeLock: true));
    }

    [Fact]
    public async Task SameSeriesKeyChange_Raises()
    {
        var f = await SeedAsync();
        await MaterializeInTransactionAsync(f, Draft(f, FirstStart), takeLock: true);

        await using var connection = new NpgsqlConnection(f.H.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE meeting_details SET original_start = original_start + interval '1 day'", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Contains("trg_meeting_details_key_immutable", error.MessageText);
    }

    [Fact]
    public async Task Remap_RejectsNonIncreasingStarts()
    {
        var f = await SeedAsync();
        var a = (await MaterializeInTransactionAsync(f, Draft(f, FirstStart), takeLock: true))!.Value;
        var b = (await MaterializeInTransactionAsync(f, Draft(f, FirstStart.AddDays(7)), takeLock: true))!.Value;
        MeetingSeries target;
        await using (var db = f.H.NewContext())
        {
            target = f.H.AddSeries(db, f.At, FirstStart);
            await db.SaveChangesAsync();
        }

        await using (var db = f.H.NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var shared = FirstStart.AddHours(2);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new EfMeetingOccurrenceWriter(db).RemapIntoSeriesAsync(
                f.H.TenantId, target.Id,
                [(a, shared, shared, shared.AddMinutes(30)), (b, shared, shared, shared.AddMinutes(30))],
                CancellationToken.None));
            Assert.Contains("strictly increasing", error.Message);
        }

        Assert.Equal(1, await DetailsAtAsync(f, f.Series.Id, FirstStart));
        Assert.Equal(1, await DetailsAtAsync(f, f.Series.Id, FirstStart.AddDays(7)));
        Assert.Equal(0, await CountAsync(f, $"SELECT count(*) FROM meeting_details WHERE series_id = '{target.Id}'"));
    }

    [Fact]
    public async Task Suppression_BlocksMaterialize_EvenUnderConcurrency()
    {
        var f = await SeedAsync();
        await using (var db = f.H.NewContext())
        {
            var elsewhere = f.H.AddSeries(db, f.At, FirstStart.AddDays(1));
            var (movedTask, _) = f.H.AddOccurrence(db, elsewhere, f.At);
            db.MeetingOccurrenceSuppressions.Add(new MeetingOccurrenceSuppression
            {
                SeriesId = f.Series.Id, OriginalStart = FirstStart, TenantId = f.H.TenantId,
                MovedToTaskId = movedTask.Id, CreatedByEmployeeId = f.At.OrganizerEmployeeId,
            });
            await db.SaveChangesAsync();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(
            () => MaterializeInTransactionAsync(f, Draft(f, FirstStart, $"M-sup-{i}"), takeLock: i % 2 == 0))));

        Assert.All(results, Assert.Null);
        Assert.Equal(0, await DetailsAtAsync(f, f.Series.Id, FirstStart));
        Assert.Equal(0, await OrphanMeetingTasksAsync(f));
    }
}
