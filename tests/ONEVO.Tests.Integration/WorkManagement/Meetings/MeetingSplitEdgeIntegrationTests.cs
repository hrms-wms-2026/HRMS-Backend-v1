using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Final-review I1-I3: a split never resets a rescheduled occurrence or touches one that has started; a
/// soft-deleted surplus occurrence is gone for every later edit; cancelling a series leaves the meetings that already
/// happened as they were.</summary>
public sealed class MeetingSplitEdgeIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(6);   // week 0 is past, week 1 is next

    private sealed record World(MyTeamDb H, MeetingSeries Series, MeetingPlacement At, Guid Week0, Guid Week1, Guid Week2, Guid Organizer);

    private static async Task<World> SeedAsync(string rule = "FREQ=WEEKLY", Action<MeetingDetails>? week2 = null)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var organizer = h.AddEmployee(db);
        var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, rule);
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);
        var (w0, _) = h.AddOccurrence(db, series, at, First);
        var (w1, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        var (w2, d2) = h.AddOccurrence(db, series, at, First.AddDays(14));
        week2?.Invoke(d2);
        await db.SaveChangesAsync();
        return new World(h, series, at with { OrganizerEmployeeId = organizer.Id }, w0.Id, w1.Id, w2.Id, organizer.Id);
    }

    private static async Task<ONEVO.Application.Common.Models.Result<MeetingEditResponse>> EditAsync(World w, Guid seriesId,
        DateTimeOffset at, EditMeetingRequest edit)
    {
        await using var db = w.H.NewContext();
        return await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(
            w.H.TenantId, w.Organizer, Guid.NewGuid(), seriesId, at, edit, CancellationToken.None);
    }

    private static EditMeetingRequest Following(string? title = null, DateTimeOffset? start = null, string? rule = null)
        => new(MeetingScopes.Following, 0, title, null, null, null, start, null, null, null, rule);

    private static void Rescheduled(MeetingDetails d, TimeSpan by, int minutes = 30)
    {
        d.StartAt = d.OriginalStart + by;
        d.EndAt = d.StartAt.AddMinutes(minutes);
        d.IsDetached = true;
    }

    [Fact]
    public async Task ATitleOnlySplit_KeepsARescheduledOccurrencesOwnTime()
    {
        var w = await SeedAsync(week2: d => Rescheduled(d, TimeSpan.FromHours(5), minutes: 50));

        var edited = await EditAsync(w, w.Series.Id, First.AddDays(7), Following(title: "Renamed"));

        Assert.True(edited.IsSuccess, edited.Error);
        await using var check = w.H.NewContext();
        var week2 = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Week2);
        Assert.Equal((First.AddDays(14).AddHours(5), First.AddDays(14).AddHours(5).AddMinutes(50)), (week2.StartAt, week2.EndAt));
    }

    [Fact]
    public async Task ATimeChangingSplit_ShiftsARescheduledOccurrenceByTheSameAmount()
    {
        var w = await SeedAsync(week2: d => Rescheduled(d, TimeSpan.FromHours(5)));

        var edited = await EditAsync(w, w.Series.Id, First.AddDays(7), Following(start: First.AddDays(7).AddHours(1)));

        Assert.True(edited.IsSuccess, edited.Error);
        await using var check = w.H.NewContext();
        var week2 = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Week2);
        Assert.Equal(First.AddDays(14).AddHours(6), week2.StartAt);
    }

    [Fact]
    public async Task AnOccurrenceThatAlreadyStarted_IsLeftWhereItIs_AndItsSlotIsNotRecreated()
    {
        // Week 2 was pulled forward to an hour ago: it has happened, though its original slot is after the split point.
        var w = await SeedAsync(week2: d => { d.StartAt = Now.AddHours(-1); d.EndAt = Now.AddMinutes(-30); d.IsDetached = true; });

        var edited = await EditAsync(w, w.Series.Id, First.AddDays(7), Following(start: First.AddDays(7).AddHours(1)));

        Assert.True(edited.IsSuccess, edited.Error);
        await using var check = w.H.NewContext();
        var week2 = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Week2);
        Assert.Equal((w.Series.Id, Now.AddHours(-1)), (week2.SeriesId, week2.StartAt));
        var mappedSlot = First.AddDays(14).AddHours(1);
        Assert.True(await check.MeetingOccurrenceSuppressions.AnyAsync(s => s.SeriesId == edited.Value!.SeriesId && s.OriginalStart == mappedSlot));
        Assert.False(await check.MeetingDetails.AnyAsync(d => d.SeriesId == edited.Value!.SeriesId && d.OriginalStart == mappedSlot));
    }

    [Fact]
    public async Task ASecondSplit_AfterASoftDeletedSurplus_Succeeds()
    {
        var w = await SeedAsync("FREQ=WEEKLY;COUNT=4");
        await using (var db = w.H.NewContext())
        {
            w.H.AddOccurrence(db, w.Series, w.At, First.AddDays(21));
            await db.SaveChangesAsync();
        }
        // From week 2 only one occurrence is left: week 3 (plain) becomes surplus and is soft-deleted.
        Assert.True((await EditAsync(w, w.Series.Id, First.AddDays(14), Following(rule: "FREQ=WEEKLY;COUNT=1"))).IsSuccess);

        var second = await EditAsync(w, w.Series.Id, First.AddDays(7), Following(title: "Renamed") with { Revision = 1 });

        Assert.True(second.IsSuccess, second.Error);
    }

    [Fact]
    public async Task EditingASoftDeletedSlot_Is404()
    {
        var w = await SeedAsync("FREQ=WEEKLY;COUNT=4");
        await using (var db = w.H.NewContext())
        {
            w.H.AddOccurrence(db, w.Series, w.At, First.AddDays(21));
            await db.SaveChangesAsync();
        }
        Assert.True((await EditAsync(w, w.Series.Id, First.AddDays(14), Following(rule: "FREQ=WEEKLY;COUNT=1"))).IsSuccess);

        var edit = await EditAsync(w, w.Series.Id, First.AddDays(21),
            new EditMeetingRequest(MeetingScopes.This, 0, "Ghost", null, null, null, null, null, null, null, null));

        Assert.Equal((404, MeetingErrorCodes.NotFound), (edit.StatusCode, edit.ErrorCode));
    }

    [Fact]
    public async Task CancellingTheSeries_LeavesPastOccurrencesCompleted_AndTheirNotesEditable()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var cancelled = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First.AddDays(7), MeetingScopes.All, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
        }

        var notes = await EditAsync(w, w.Series.Id, First,
            new EditMeetingRequest(MeetingScopes.This, 0, null, "Minutes: shipped.", null, null, null, null, null, null, null));

        Assert.True(notes.IsSuccess, notes.Error);
        await using var check = w.H.NewContext();
        var reads = new MeetingTestServices(check, new MeetingTestClock(Now));
        var past = await new ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingReadService(
            reads.Meetings, reads.Access, reads.Projects, new ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.EfObjectiveRepository(check),
            new ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository(check), reads.Recurrence, reads.Clock)
            .GetOccurrenceAsync(w.H.TenantId, w.Organizer, w.Week0, CancellationToken.None);
        Assert.True(past.IsSuccess, past.Error);
        Assert.Equal(MeetingStatuses.Completed, past.Value!.Status);
        var tasks = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).ToDictionaryAsync(t => t.Id);
        Assert.Equal("Minutes: shipped.", tasks[w.Week0].Description);
    }
}
