using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.5 forward splits: "this and following" and a time edit of "all" cut the series at P, keep every
/// occurrence's task id, leave history on the old series, and carry suppressions and attendees across.</summary>
public sealed class MeetingSplitIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(6);   // week 0 is past, week 1 is next

    private sealed record World(MyTeamDb H, MeetingSeries Series, MeetingPlacement At, Guid Week0, Guid Week1, Guid Week2, Guid Organizer, Guid Attendee);

    private static async Task<World> SeedAsync(string rule = "FREQ=WEEKLY")
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, rule);
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, attendee.Id, CalendarEventParticipantStatuses.Accepted);
        var (w0, _) = h.AddOccurrence(db, series, at, First);
        var (w1, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        var (w2, _) = h.AddOccurrence(db, series, at, First.AddDays(14));
        await db.SaveChangesAsync();
        return new World(h, series, at with { OrganizerEmployeeId = organizer.Id }, w0.Id, w1.Id, w2.Id, organizer.Id, attendee.Id);
    }

    private static async Task<MeetingEditResponse> EditAsync(World w, DateTimeOffset at, EditMeetingRequest edit)
    {
        await using var db = w.H.NewContext();
        var result = await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(
            w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, at, edit, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static EditMeetingRequest Following(string? title = null, DateTimeOffset? start = null)
        => new(MeetingScopes.Following, 0, title, null, null, null, start, null, null, null, null);

    [Fact]
    public async Task EditFollowing_KeepsTaskIds_AndHistoryStaysOnTheOldSeries()
    {
        var w = await SeedAsync();

        var edited = await EditAsync(w, First.AddDays(7), Following(title: "Sync v2", start: First.AddDays(7).AddHours(1)));

        Assert.True(edited.Split);
        Assert.Equal(w.Week1, edited.OccurrenceTaskId);
        await using var check = w.H.NewContext();
        var details = await check.MeetingDetails.ToDictionaryAsync(d => d.TaskId);
        Assert.Equal(w.Series.Id, details[w.Week0].SeriesId);
        Assert.Equal(First, details[w.Week0].StartAt);
        Assert.Equal((edited.SeriesId, First.AddDays(7).AddHours(1)), (details[w.Week1].SeriesId, details[w.Week1].OriginalStart));
        Assert.Equal((edited.SeriesId, First.AddDays(14).AddHours(1)), (details[w.Week2].SeriesId, details[w.Week2].OriginalStart));
        var target = await check.MeetingSeries.SingleAsync(s => s.Id == edited.SeriesId);
        Assert.Equal((w.Series.Id, MeetingSplitReasons.EditFollowing, "Sync v2"), (target.SplitFromSeriesId, target.SplitReason, target.Title));
        var source = await check.MeetingSeries.SingleAsync(s => s.Id == w.Series.Id);
        Assert.Contains("UNTIL=20261109T085959Z", source.RecurrenceRule);
        var tasks = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).ToDictionaryAsync(t => t.Id);
        Assert.Equal(("Sync v2", w.Series.Title), (tasks[w.Week2].Title, tasks[w.Week0].Title));
    }

    [Fact]
    public async Task ATimeEditOfAll_SplitsAtTheFirstOccurrenceNotStarted()
    {
        var w = await SeedAsync();

        var edited = await EditAsync(w, First.AddDays(14),
            new EditMeetingRequest(MeetingScopes.All, 0, null, null, null, null, First.AddDays(14).AddHours(-1), null, null, null, null));

        await using var check = w.H.NewContext();
        var details = await check.MeetingDetails.ToDictionaryAsync(d => d.TaskId);
        Assert.Equal(First.AddDays(7).AddHours(-1), details[w.Week1].OriginalStart);   // P = week 1, the first not started
        Assert.Equal(edited.SeriesId, details[w.Week1].SeriesId);
        Assert.Equal(w.Series.Id, details[w.Week0].SeriesId);
    }

    [Fact]
    public async Task SurplusWithAnOverride_IsCancelled_PlainSurplusIsSoftDeleted()
    {
        var w = await SeedAsync("FREQ=WEEKLY;COUNT=4");
        Guid week3;
        await using (var db = w.H.NewContext())
        {
            (var t3, _) = w.H.AddOccurrence(db, w.Series, w.At, First.AddDays(21));
            week3 = t3.Id;
            w.H.AddEmployeeOverride(db, w.Week2, w.Attendee, MeetingAttendeeChanges.Remove);
            await db.SaveChangesAsync();
        }

        // From week 1 on, only one more occurrence: week 2 (with an override) and week 3 (plain) become surplus.
        await EditAsync(w, First.AddDays(7), new EditMeetingRequest(MeetingScopes.Following, 0, null, null, null, null, null, null,
            null, null, "FREQ=WEEKLY;COUNT=1"));

        await using var check = w.H.NewContext();
        var details = await check.MeetingDetails.ToDictionaryAsync(d => d.TaskId);
        Assert.NotNull(details[w.Week2].CancelledAt);
        var week3Task = await check.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == week3);
        Assert.True(week3Task.IsDeleted);
        Assert.False((await check.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == w.Week2)).IsDeleted);
    }

    [Fact]
    public async Task Suppressions_AreCopiedAfterThePoint()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            db.MeetingOccurrenceSuppressions.Add(new MeetingOccurrenceSuppression
            {
                SeriesId = w.Series.Id, OriginalStart = First.AddDays(21), TenantId = w.H.TenantId, MovedToTaskId = w.Week0,
                CreatedByEmployeeId = w.Organizer,
            });
            await db.SaveChangesAsync();
        }

        var edited = await EditAsync(w, First.AddDays(7), Following(start: First.AddDays(7).AddHours(3)));

        await using var check = w.H.NewContext();
        Assert.True(await check.MeetingOccurrenceSuppressions.AnyAsync(
            s => s.SeriesId == edited.SeriesId && s.OriginalStart == First.AddDays(21).AddHours(3)));
    }

    [Fact]
    public async Task Rsvps_ResetOnlyWhenTheTimeOrLengthChanges()
    {
        var w = await SeedAsync();

        var titleOnly = await EditAsync(w, First.AddDays(14), Following(title: "Renamed"));
        var w2 = await SeedAsync();
        var moved = await EditAsync(w2, First.AddDays(7), Following(start: First.AddDays(7).AddMinutes(30)));

        await using var check = w.H.NewContext();
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, (await check.CalendarEventParticipants
            .SingleAsync(p => p.MeetingSeriesId == titleOnly.SeriesId && p.EmployeeId == w.Attendee)).ResponseStatus);
        await using var check2 = w2.H.NewContext();
        Assert.Equal(CalendarEventParticipantStatuses.Pending, (await check2.CalendarEventParticipants
            .SingleAsync(p => p.MeetingSeriesId == moved.SeriesId && p.EmployeeId == w2.Attendee)).ResponseStatus);
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, (await check2.CalendarEventParticipants
            .SingleAsync(p => p.MeetingSeriesId == moved.SeriesId && p.EmployeeId == w2.Organizer)).ResponseStatus);
    }
}
