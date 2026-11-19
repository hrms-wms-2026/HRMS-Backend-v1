using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.3: edit one occurrence (detaches it), the whole series (future non-detached ones only), notes after
/// the meeting (D19, audited), and stale revisions (409 meeting_changed).</summary>
public sealed class MeetingEditIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed record World(MyTeamDb H, MeetingSeries Series, Guid Past, Guid Next, Guid Later, Guid Organizer, Guid Attendee, Guid Outsider);

    /// <summary>Weekly series; Past (week 0) already happened at "now" = week 1 minus a day; Next = week 1, Later = week 2.</summary>
    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        var outsider = h.AddEmployee(db);
        var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, attendee.Id);
        var (past, _) = h.AddOccurrence(db, series, at, First);
        var (next, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        var (later, _) = h.AddOccurrence(db, series, at, First.AddDays(14));
        await db.SaveChangesAsync();
        return new World(h, series, past.Id, next.Id, later.Id, organizer.Id, attendee.Id, outsider.Id);
    }

    private static MeetingTestClock Clock() => new(First.AddDays(6));

    private static EditMeetingRequest Edit(string scope, int revision, string? title = null, string? description = null,
        DateTimeOffset? start = null, string? location = null)
        => new(scope, revision, title, description, location, null, start, null, null, null, null);

    [Fact]
    public async Task EditThis_ChangesAndDetachesOnlyThatOccurrence()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var result = await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First.AddDays(7), Edit(MeetingScopes.This, 0, title: "Moved to room 5", location: "Room 5",
                    start: First.AddDays(7).AddHours(2)), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        await using var check = w.H.NewContext();
        var next = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Next);
        var later = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Later);
        Assert.Equal((true, 1, First.AddDays(7).AddHours(2), First.AddDays(7)), (next.IsDetached, next.Revision, next.StartAt, next.OriginalStart));
        Assert.Equal(next.StartAt.AddMinutes(w.Series.DurationMinutes), next.EndAt);
        Assert.False(later.IsDetached);
        var tasks = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).ToDictionaryAsync(t => t.Id);
        Assert.Equal("Moved to room 5", tasks[w.Next].Title);
        Assert.Equal(w.Series.Title, tasks[w.Later].Title);
    }

    [Fact]
    public async Task EditAll_NonKeyFields_UpdateTheSeriesAndFutureNonDetachedOnly()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var services = new MeetingTestServices(db, Clock());
            Assert.True((await services.Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First.AddDays(14),
                Edit(MeetingScopes.This, 0, title: "Detached"), CancellationToken.None)).IsSuccess);
        }
        await using (var db = w.H.NewContext())
        {
            var result = await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First.AddDays(7), Edit(MeetingScopes.All, 0, title: "Weekly sync v2", description: "New agenda"),
                CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            Assert.False(result.Value!.Split);
        }

        await using var check = w.H.NewContext();
        var tasks = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).ToDictionaryAsync(t => t.Id);
        Assert.Equal("Weekly sync v2", tasks[w.Next].Title);
        Assert.Equal("Detached", tasks[w.Later].Title);           // edited on its own: no longer follows the series
        Assert.Equal(w.Series.Title, tasks[w.Past].Title);        // history never changes
        var series = await check.MeetingSeries.SingleAsync();
        Assert.Equal(("Weekly sync v2", "New agenda", 1), (series.Title, series.Description, series.Revision));
    }

    [Fact]
    public async Task RescheduleThis_MovesItsAttendanceCheck_WithTheNewEnd()
    {
        // The first attendance check is 30 min after the end. Moving the occurrence two hours later moves that check too;
        // left at the old time, it ran before the meeting had ended.
        var w = await SeedAsync();
        DateTimeOffset oldCheck;
        await using (var db = w.H.NewContext())
        {
            var details = await db.MeetingDetails.SingleAsync(d => d.TaskId == w.Next);
            details.AttendanceStatus = MeetingAttendanceStatuses.Pending;
            details.AttendanceNextCheckAt = oldCheck = details.EndAt.AddMinutes(30);
            await db.SaveChangesAsync();
        }

        await using (var db = w.H.NewContext())
            Assert.True((await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First.AddDays(7), Edit(MeetingScopes.This, 0, start: First.AddDays(7).AddHours(2)),
                CancellationToken.None)).IsSuccess);

        await using var check = w.H.NewContext();
        var moved = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Next);
        Assert.Equal(oldCheck.AddHours(2), moved.AttendanceNextCheckAt);
    }

    [Theory]
    [InlineData(MeetingScopes.This)]
    [InlineData(MeetingScopes.All)]
    public async Task ALocationLongerThanItsColumn_Is400_NotAServerError(string scope)
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
            w.Series.Id, First.AddDays(7), Edit(scope, 0, location: new string('x', 501)), CancellationToken.None);

        Assert.Equal((400, MeetingErrorCodes.LocationInvalid), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task AStaleRevision_Is409MeetingChanged()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            Assert.True((await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First.AddDays(7), Edit(MeetingScopes.This, 0, title: "First tab"), CancellationToken.None)).IsSuccess);

        await using var second = w.H.NewContext();
        var result = await new MeetingTestServices(second, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
            w.Series.Id, First.AddDays(7), Edit(MeetingScopes.This, 0, title: "Second tab"), CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.Changed), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task APastOccurrence_RejectsATimeEdit_ButTakesNotesWithAnAuditRow()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var services = new MeetingTestServices(db, Clock());
            var time = await services.Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First,
                Edit(MeetingScopes.This, 0, start: First.AddDays(10)), CancellationToken.None);
            Assert.Equal((409, MeetingErrorCodes.OccurrenceStarted), (time.StatusCode, time.ErrorCode));
        }
        await using (var db = w.H.NewContext())
        {
            var notes = await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First, Edit(MeetingScopes.This, 0, description: "Minutes: shipped v1"), CancellationToken.None);
            Assert.True(notes.IsSuccess, notes.Error);
        }

        await using var check = w.H.NewContext();
        Assert.Equal("Minutes: shipped v1",
            (await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).SingleAsync(t => t.Id == w.Past)).Description);
        var log = await check.TaskEditLogs.SingleAsync(l => l.TaskId == w.Past);
        Assert.Equal((TaskEditLogSources.MeetingNotes, w.Organizer), (log.Source, log.EmployeeId));
        Assert.False((await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Past)).IsDetached);
    }

    [Fact]
    public async Task AnAttendeeGets403_AnOutsider404()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();
        var services = new MeetingTestServices(db, Clock());

        var attendee = await services.Edits.EditAsync(w.H.TenantId, w.Attendee, Guid.NewGuid(), w.Series.Id, First.AddDays(7),
            Edit(MeetingScopes.This, 0, title: "x"), CancellationToken.None);
        var outsider = await services.Edits.EditAsync(w.H.TenantId, w.Outsider, Guid.NewGuid(), w.Series.Id, First.AddDays(7),
            Edit(MeetingScopes.This, 0, title: "x"), CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.OrganizerRequired), (attendee.StatusCode, attendee.ErrorCode));
        Assert.Equal((404, MeetingErrorCodes.NotFound), (outsider.StatusCode, outsider.ErrorCode));
    }

    [Fact]
    public async Task EditingAVirtualOccurrence_MaterializesItWithItsDeterministicId()
    {
        var w = await SeedAsync();
        var third = First.AddDays(21);
        await using (var db = w.H.NewContext())
        {
            var result = await new MeetingTestServices(db, Clock()).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, third, Edit(MeetingScopes.This, 0, location: "Online"), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(ONEVO.Application.Features.WorkManagement.Meetings.MeetingOccurrenceIds.For(w.Series.Id, third), result.Value!.OccurrenceTaskId);
        }

        await using var check = w.H.NewContext();
        var details = await check.MeetingDetails.SingleAsync(d => d.OriginalStart == third);
        Assert.Equal(("Online", true), (details.Location, details.IsDetached));
    }
}
