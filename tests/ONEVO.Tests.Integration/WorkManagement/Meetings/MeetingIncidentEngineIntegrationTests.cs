using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 9 (spec 10 "Engine integration"): the C2 meeting-exception engine reads task-meeting occurrences as it
/// reads calendar events - one view per materialized occurrence keyed by its task id, so a split never duplicates an
/// incident; a private meeting's title stays masked; a synced occurrence's spans come from its own rows only.</summary>
public sealed class MeetingIncidentEngineIntegrationTests
{
    private static async Task<IReadOnlyList<CalendarMeetingView>> ViewsAsync(World w, Guid employeeId, DateTimeOffset at)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at));
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
        var source = new TaskMeetingOccurrenceSource(s.Meetings, new EfCalendarEventRepository(db), new EfMeetingAttendanceRepository(db));
        return await source.ListAsync(new EmployeeOccurrenceContext(w.H.TenantId, employee, at.AddDays(-30), at.AddDays(14),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { employee.Email! }, ReportingManagerId: null,
            new Dictionary<(Guid, DateTimeOffset), decimal>(), at), CancellationToken.None);
    }

    [Fact]
    public async Task TaskMeeting_IsEvaluated_WithTheOccurrenceTaskIdAndOriginalStart()
    {
        var w = await SeedAsync();

        var views = await ViewsAsync(w, w.Attendee, End(0).AddHours(1));

        Assert.Equal(w.Tasks.Order(), views.Select(v => v.EventId).Order());
        var first = Assert.Single(views, v => v.EventId == w.Tasks[0]);
        Assert.Equal((Week(0), End(0), "Weekly sync", false, false), (first.Start, first.End, first.Title, first.EmployeeIsOrganizer, first.IsCancelled));
        Assert.Equal((w.Organizer, CalendarEventParticipantStatuses.Pending, CalendarReportState.Pending),
            (first.OrganizerEmployeeId!.Value, first.ResponseStatus, first.Report));
        Assert.Equal(2, first.PeopleCount);
        Assert.True((await ViewsAsync(w, w.Organizer, End(0).AddHours(1))).All(v => v.EmployeeIsOrganizer));
    }

    [Fact]
    public async Task SplitDoesNotDuplicateIncidents()
    {
        var w = await SeedAsync();
        var before = (await ViewsAsync(w, w.Attendee, Now)).Select(v => (v.EventId, v.Start)).Order().ToList();

        await using (var db = w.H.NewContext())
        {
            var edit = await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(w.H.TenantId, w.Organizer, w.OrganizerUser,
                w.SeriesId, Week(1), new EditMeetingRequest(MeetingScopes.Following, 0, "Weekly sync v2", null, null, null, null,
                    null, null, null, null), CancellationToken.None);
            Assert.True(edit.IsSuccess, edit.Error);
            Assert.True(edit.Value!.Split);
        }
        var after = await ViewsAsync(w, w.Attendee, Now);

        // The incident key is (rule, event id, occurrence start): the same occurrences, the same keys - nothing new to record.
        Assert.Equal(before, after.Select(v => (v.EventId, v.Start)).Order().ToList());
        Assert.Equal("Weekly sync v2", Assert.Single(after, v => v.EventId == w.Tasks[1]).Title);
    }

    [Fact]
    public async Task PrivateTaskMeeting_TitleIsMasked()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            await db.MeetingDetails.Where(d => d.TaskId == w.Tasks[0]).ExecuteUpdateAsync(u => u.SetProperty(d => d.IsPrivate, true));
        }

        var views = await ViewsAsync(w, w.Attendee, End(0).AddHours(1));

        Assert.Equal("Private meeting", Assert.Single(views, v => v.EventId == w.Tasks[0]).Title);
        Assert.Equal("Weekly sync", Assert.Single(views, v => v.EventId == w.Tasks[1]).Title);
    }

    [Fact]
    public async Task SyncedOccurrence_SpansComeFromItsOwnRows()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub
        {
            Answer = (_, _) =>
            [
                Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0).AddMinutes(3), 20)),
                Session("r-1", Week(1), Rec("a", w.AttendeeEmail, Week(1).AddMinutes(9), 5)),
            ],
        };
        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) })
            await SyncAsync(w, stub, End(0) + after);   // week 0 settles; week 1 has not happened at these times
        await SyncAsync(w, stub, End(1).AddMinutes(30));   // week 1: first checkpoint only

        var views = await ViewsAsync(w, w.Attendee, End(1).AddHours(1));

        var synced = Assert.Single(views, v => v.EventId == w.Tasks[0]);
        Assert.Equal(CalendarReportState.Received, synced.Report);
        var span = Assert.Single(synced.Attendance);
        Assert.Equal((Week(0).AddMinutes(3), (DateTimeOffset?)Week(0).AddMinutes(23)), (span.JoinedAt, span.LeftAt));
        var provisional = Assert.Single(views, v => v.EventId == w.Tasks[1]);
        Assert.Equal((CalendarReportState.Pending, 0), (provisional.Report, provisional.Attendance.Count));
    }

    [Fact]
    public async Task AnOccurrenceWithoutALink_HasNoOnlineMeeting_AndUnavailableIsUnavailable()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            await db.MeetingDetails.Where(d => d.TaskId == w.Tasks[0])
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.AttendanceStatus, ONEVO.Domain.Features.WorkManagement.Meetings.Entities.MeetingAttendanceStatuses.Unavailable));
            await db.MeetingDetails.Where(d => d.TaskId == w.Tasks[1])
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.CalendarEventMeetingId, (Guid?)null));
        }

        var views = await ViewsAsync(w, w.Attendee, End(1).AddHours(1));

        Assert.Equal(CalendarReportState.Unavailable, Assert.Single(views, v => v.EventId == w.Tasks[0]).Report);
        Assert.Equal(CalendarReportState.NoOnlineMeeting, Assert.Single(views, v => v.EventId == w.Tasks[1]).Report);
    }
}
