using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Meeting hours (2026-10-26 design, Part A): the hourly pass stamps each ended occurrence once with its
/// person-hours and cascades them up its current module chain to the project.</summary>
public sealed class MeetingHoursStampingIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterEnd = Start.AddHours(2);

    private sealed record World(MyTeamDb H, MeetingPlacement Root, Guid Module, MeetingSeries Series, Guid TaskId);

    /// <summary>A 60-minute meeting in module M (child of the project root): organizer + 2 accepted + 1 declined.</summary>
    private static async Task<World> SeedAsync(bool cancelled = false)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var root = h.AddPlacement(db);
        var module = h.AddModule(db, root.ProjectId, root.ObjectiveId, root.OrganizerEmployeeId);
        await db.SaveChangesAsync();
        var at = root with { ObjectiveId = module };
        var series = h.AddSeries(db, at, Start);
        series.DurationMinutes = 60;
        h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, h.AddEmployee(db).Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, h.AddEmployee(db).Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, h.AddEmployee(db).Id, CalendarEventParticipantStatuses.Rejected);
        var (task, details) = h.AddOccurrence(db, series, at);
        if (cancelled)
            details.CancelledAt = Start.AddDays(-1);
        await db.SaveChangesAsync();
        return new World(h, root, module, series, task.Id);
    }

    private static async Task<int> StampAsync(MyTeamDb h, DateTimeOffset now, IJobLeaseService? lease = null)
        => (await RunAsync(h, now, lease)).Stamped;

    private static async Task<MeetingHoursStampRun> RunAsync(MyTeamDb h, DateTimeOffset now, IJobLeaseService? lease = null)
    {
        await using var db = h.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(now));
        var stamper = new MeetingHoursStamper(s.Meetings, s.Writer, s.Materializer, s.Recurrence, new EfObjectiveRepository(db),
            s.Projects, s.UnitOfWork, s.Clock, lease ?? new HeldLease(int.MaxValue), NullLogger<MeetingHoursStamper>.Instance);
        return await stamper.StampTenantAsync(h.TenantId, CancellationToken.None);
    }

    /// <summary>The job's lease: renews <paramref name="renewals"/> times, then reports it lost.</summary>
    private sealed class HeldLease(int renewals) : IJobLeaseService
    {
        private int _left = renewals;
        public string InstanceId => "test";
        public Task<bool> TryAcquireAsync(string jobName, TimeSpan ttl, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RenewAsync(string jobName, TimeSpan ttl, CancellationToken ct) => Task.FromResult(_left-- > 0);
        public Task ReleaseAsync(string jobName, CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task<(decimal? Estimated, decimal Completed, DateTimeOffset? StampedAt)> TaskStateAsync(MyTeamDb h, Guid taskId)
    {
        await using var db = h.NewContext();
        var task = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == taskId);
        var details = await db.MeetingDetails.SingleAsync(d => d.TaskId == taskId);
        return (task.EstimatedHours, task.CompletedHours, details.HoursStampedAt);
    }

    private static async Task<(decimal Module, decimal Root, decimal Project)> RollupAsync(MyTeamDb h, World w, Guid? module = null)
    {
        await using var db = h.NewContext();
        var m = await db.Objectives.SingleAsync(o => o.Id == (module ?? w.Module));
        var r = await db.Objectives.SingleAsync(o => o.Id == w.Root.ObjectiveId);
        var p = await db.Projects.SingleAsync(x => x.Id == w.Root.ProjectId);
        return (m.CompletedHours, r.CompletedHours, p.CompletedHours);
    }

    [Fact]
    public async Task An_ended_occurrence_gets_person_hours_and_cascades()
    {
        var w = await SeedAsync();

        Assert.Equal(1, await StampAsync(w.H, AfterEnd));

        var (estimated, completed, stampedAt) = await TaskStateAsync(w.H, w.TaskId);
        Assert.Equal((3m, 3m), (estimated, completed));
        Assert.NotNull(stampedAt);
        Assert.Equal((3m, 3m, 3m), await RollupAsync(w.H, w));
    }

    [Fact]
    public async Task Stamping_twice_adds_hours_once()
    {
        var w = await SeedAsync();

        await StampAsync(w.H, AfterEnd);
        Assert.Equal(0, await StampAsync(w.H, AfterEnd.AddHours(1)));

        Assert.Equal((3m, 3m, 3m), await RollupAsync(w.H, w));
    }

    [Fact]
    public async Task A_future_or_running_occurrence_is_not_stamped()
    {
        var w = await SeedAsync();

        Assert.Equal(0, await StampAsync(w.H, Start.AddMinutes(30)));

        var (estimated, _, stampedAt) = await TaskStateAsync(w.H, w.TaskId);
        Assert.Null(estimated);
        Assert.Null(stampedAt);
    }

    [Fact]
    public async Task A_cancelled_occurrence_is_stamped_with_zero()
    {
        var w = await SeedAsync(cancelled: true);

        Assert.Equal(1, await StampAsync(w.H, AfterEnd));

        var (estimated, completed, stampedAt) = await TaskStateAsync(w.H, w.TaskId);
        Assert.Equal((0m, 0m), (estimated, completed));
        Assert.NotNull(stampedAt);
        Assert.Equal((0m, 0m, 0m), await RollupAsync(w.H, w));
    }

    [Fact]
    public async Task Cascades_up_the_current_module_chain()
    {
        var w = await SeedAsync();
        Guid other;
        await using (var db = w.H.NewContext())
        {
            other = w.H.AddModule(db, w.Root.ProjectId, w.Root.ObjectiveId, w.Root.OrganizerEmployeeId);
            await db.SaveChangesAsync();
            // Moved before it ended: series and its occurrence move together (the placement trigger checks at commit).
            (await db.MeetingSeries.SingleAsync(s => s.Id == w.Series.Id)).ObjectiveId = other;
            (await db.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == w.TaskId)).ObjectiveId = other;
            await db.SaveChangesAsync();
        }

        await StampAsync(w.H, AfterEnd);

        Assert.Equal((3m, 3m, 3m), await RollupAsync(w.H, w, other));
        Assert.Equal(0m, (await RollupAsync(w.H, w)).Module);
    }

    [Fact]
    public async Task A_task_completed_between_two_stamps_is_not_lost()
    {
        // One job run reuses its DbContext: a module tracked by the first stamp must not overwrite a user's later write.
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var at = w.Root with { ObjectiveId = w.Module };
            var second = w.H.AddSeries(db, at, Start.AddHours(3));
            second.DurationMinutes = 60;
            w.H.AddSeriesParticipant(db, second, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
            w.H.AddOccurrence(db, second, at);
            await db.SaveChangesAsync();
        }

        await using var jobDb = w.H.NewContext();
        var s = new MeetingTestServices(jobDb, new MeetingTestClock(AfterEnd));
        var stamper = new MeetingHoursStamper(s.Meetings, s.Writer, s.Materializer, s.Recurrence, new EfObjectiveRepository(jobDb),
            s.Projects, s.UnitOfWork, s.Clock, new HeldLease(int.MaxValue), NullLogger<MeetingHoursStamper>.Instance);
        await stamper.StampTenantAsync(w.H.TenantId, CancellationToken.None);   // first meeting: +3

        await using (var user = w.H.NewContext())
        {
            (await user.Objectives.SingleAsync(o => o.Id == w.Module)).CompletedHours += 5m;   // a user completes a 5 h task
            await user.SaveChangesAsync();
        }

        ((MeetingTestClock)s.Clock).UtcNow = Start.AddHours(5);
        await stamper.StampTenantAsync(w.H.TenantId, CancellationToken.None);   // second meeting: +1

        Assert.Equal(9m, (await RollupAsync(w.H, w)).Module);
    }

    [Fact]
    public async Task An_all_day_meeting_counts_a_workday_per_person()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var details = await db.MeetingDetails.SingleAsync(d => d.TaskId == w.TaskId);
            details.IsAllDay = true;
            details.StartAt = new DateTimeOffset(Start.Date, TimeSpan.Zero);
            details.EndAt = new DateTimeOffset(Start.Date.AddDays(1), TimeSpan.Zero);
            await db.SaveChangesAsync();
        }

        await StampAsync(w.H, new DateTimeOffset(Start.Date.AddDays(1).AddHours(1), TimeSpan.Zero));

        Assert.Equal(24m, (await TaskStateAsync(w.H, w.TaskId)).Estimated);   // 3 people x 8 h
    }

    [Fact]
    public async Task An_ended_virtual_occurrence_is_materialized_and_stamped()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid seriesId;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            await db.SaveChangesAsync();
            var series = h.AddSeries(db, at, Start, "FREQ=WEEKLY");
            series.DurationMinutes = 60;
            h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
            seriesId = series.Id;
            await db.SaveChangesAsync();   // never materialized
        }

        Assert.Equal(1, await StampAsync(h, AfterEnd));

        await using var check = h.NewContext();
        var details = await check.MeetingDetails.SingleAsync(d => d.SeriesId == seriesId);
        var task = await check.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == details.TaskId);
        Assert.Equal(Start, details.OriginalStart);
        Assert.Equal((1m, WorkTaskKinds.Meeting), (task.EstimatedHours!.Value, task.TaskKind));
        Assert.NotNull(details.HoursStampedAt);
    }

    [Fact]
    public async Task A_lost_lease_stops_the_run_after_the_occurrence_in_hand()
    {
        // Plan A minor M1: a long first run renews the job lease as it goes; once the lease is lost (another instance
        // may now run), it stops instead of stamping on alongside that instance.
        var h = await MyTeamDb.CreateAsync();
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            var series = h.AddSeries(db, at, Start);
            h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
            h.AddOccurrence(db, series, at);
            var other = h.AddSeries(db, at, Start);
            h.AddSeriesParticipant(db, other, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
            h.AddOccurrence(db, other, at);
            await db.SaveChangesAsync();
        }

        var run = await RunAsync(h, AfterEnd, new HeldLease(renewals: 0));

        Assert.Equal((1, true), (run.Stamped, run.LeaseLost));
    }

    [Fact]
    public async Task A_series_cancelled_before_its_slots_were_generated_still_gets_the_meetings_it_held()
    {
        // Plan A minor M6: the daily series met at Start, was never materialized (job off), and was cancelled before its
        // second meeting. The one that took place still gets its occurrence and hours; the ones after the cancel do not.
        var h = await MyTeamDb.CreateAsync();
        Guid seriesId;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            await db.SaveChangesAsync();
            var series = h.AddSeries(db, at, Start, "FREQ=DAILY");
            series.DurationMinutes = 60;
            series.CancelledAt = Start.AddDays(1).AddHours(-1);
            h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
            seriesId = series.Id;
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await StampAsync(h, Start.AddDays(3)));

        await using var check = h.NewContext();
        var details = await check.MeetingDetails.Where(d => d.SeriesId == seriesId).ToListAsync();
        Assert.Equal([Start], details.Select(d => d.OriginalStart));
    }
}
