using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.5 generator: the next 7 days, idempotent, never for a cancelled series or a suppressed slot, never
/// for a slot a split took away (lock, re-read, write), and it stops when its lease is lost.</summary>
public sealed class MeetingGeneratorIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddHours(-1);

    private sealed class LeaseStub(int renewalsAllowed = int.MaxValue) : IJobLeaseService
    {
        private int _renewals;
        public string InstanceId => "test";
        public Task<bool> TryAcquireAsync(string jobName, TimeSpan ttl, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RenewAsync(string jobName, TimeSpan ttl, CancellationToken ct) => Task.FromResult(++_renewals <= renewalsAllowed);
        public Task ReleaseAsync(string jobName, CancellationToken ct) => Task.CompletedTask;
    }

    private static MeetingOccurrenceGenerator Generator(ApplicationDbContext db, IJobLeaseService? lease = null)
    {
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        return new MeetingOccurrenceGenerator(s.Meetings, s.Writer, s.Materializer, s.Recurrence, lease ?? new LeaseStub(), s.UnitOfWork,
            s.Clock, NullLogger<MeetingOccurrenceGenerator>.Instance);
    }

    private static async Task<(MyTeamDb H, MeetingSeries Series, MeetingPlacement At)> SeedDailyAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var series = h.AddSeries(db, at, First, "FREQ=DAILY");
        h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
        h.AddOccurrence(db, series, at, First);
        await db.SaveChangesAsync();
        return (h, series, at);
    }

    private static async Task<int> CountAsync(MyTeamDb h, Guid seriesId)
    {
        await using var db = h.NewContext();
        return await db.MeetingDetails.CountAsync(d => d.SeriesId == seriesId);
    }

    [Fact]
    public async Task FillsTheNextSevenDays_Idempotently()
    {
        var (h, series, _) = await SeedDailyAsync();

        await using (var db = h.NewContext())
            Assert.True(await Generator(db).RunForTenantAsync(h.TenantId, CancellationToken.None));
        var afterFirst = await CountAsync(h, series.Id);
        await using (var db = h.NewContext())
            await Generator(db).RunForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Equal(7, afterFirst);   // daily from 1 h ahead: 7 starts inside [now, now + 7 days)
        Assert.Equal(afterFirst, await CountAsync(h, series.Id));
    }

    [Fact]
    public async Task SkipsCancelledSeries_AndSuppressedSlots()
    {
        var (h, series, at) = await SeedDailyAsync();
        MeetingSeries cancelled;
        await using (var db = h.NewContext())
        {
            cancelled = h.AddSeries(db, at, First, "FREQ=DAILY");
            cancelled.CancelledAt = Now.AddDays(-1);
            db.MeetingOccurrenceSuppressions.Add(new MeetingOccurrenceSuppression
            {
                SeriesId = series.Id, OriginalStart = First.AddDays(2), TenantId = h.TenantId,
                MovedToTaskId = (await db.MeetingDetails.FirstAsync()).TaskId, CreatedByEmployeeId = at.OrganizerEmployeeId,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = h.NewContext())
            await Generator(db).RunForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Equal(0, await CountAsync(h, cancelled.Id));
        Assert.Equal(6, await CountAsync(h, series.Id));
        await using var check = h.NewContext();
        Assert.False(await check.MeetingDetails.AnyAsync(d => d.SeriesId == series.Id && d.OriginalStart == First.AddDays(2)));
    }

    [Fact]
    public async Task AStaleWorkList_CannotResurrectASplitSlot()
    {
        var (h, series, _) = await SeedDailyAsync();
        IReadOnlyList<Guid> scanned;
        await using (var db = h.NewContext())
            scanned = await new MeetingTestServices(db, new MeetingTestClock(Now)).Meetings.ListActiveRecurringSeriesIdsAsync(h.TenantId);

        // A split commits after the generator's scan and before its per-series step.
        await using (var db = h.NewContext())
            Assert.True((await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(h.TenantId, series.OrganizerEmployeeId,
                Guid.NewGuid(), series.Id, First.AddDays(3), new EditMeetingRequest(MeetingScopes.Following, 0, "Later",
                    null, null, null, null, null, null, null, null), CancellationToken.None)).IsSuccess);

        await using (var db = h.NewContext())
            await Generator(db).GenerateSeriesAsync(h.TenantId, scanned.Single(), CancellationToken.None);

        await using var check = h.NewContext();
        Assert.False(await check.MeetingDetails.AnyAsync(d => d.SeriesId == series.Id && d.OriginalStart >= First.AddDays(3)));
    }

    [Fact]
    public async Task ALostLease_StopsTheRun()
    {
        var (h, first, at) = await SeedDailyAsync();
        MeetingSeries second;
        await using (var db = h.NewContext())
        {
            second = h.AddSeries(db, at, First.AddMinutes(30), "FREQ=DAILY");
            await db.SaveChangesAsync();
        }

        bool completed;
        await using (var db = h.NewContext())
            completed = await Generator(db, new LeaseStub(renewalsAllowed: 0)).RunForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.False(completed);
        Assert.Equal(7, await CountAsync(h, first.Id));
        Assert.Equal(0, await CountAsync(h, second.Id));
    }
}
